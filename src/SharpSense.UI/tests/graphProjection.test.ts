import assert from "node:assert/strict";
import test from "node:test";
import { GraphProjection } from "../src/features/explorer/graphProjection";
import { GraphBufferStore } from "../src/features/explorer/rendering/GraphBufferStore";
import type { GraphEdge, GraphNode, GraphPage } from "../src/shared/api/models";

function node(id: number, type = "class", label = `Symbol${id}`): GraphNode {
  return {
    id: String(id),
    codeNodeId: id,
    type,
    label,
    relativePath: "src/Graph.cs",
    projectId: "1",
    scope: "selected",
    isClickable: true,
  };
}
function edge(source: number, target: number): GraphEdge {
  return {
    id: `${source}|${target}`,
    source: String(source),
    target: String(target),
    type: "methodcall",
    scope: "internal",
  };
}
function page<T>(items: T[], revision = "r1"): GraphPage<T> {
  items.forEach(Object.freeze);
  Object.freeze(items);
  return Object.freeze({ items, revision, nextCursor: null, totalCount: null });
}

test("append projection retains immutable snapshots, type counts and bounded ID lookups", () => {
  const store = new GraphProjection();
  const first = page([node(1), node(2, "method")]);
  const edgePage = page([edge(1, 3)]);
  const input = {
    nodePages: [first],
    edgePages: [edgePage],
    types: ["class"],
    search: "",
  };
  store.update(input);
  const before = store.getSnapshot();
  assert.deepEqual(
    before.data.nodes.map((value) => value.id),
    ["1"],
  );
  assert.equal(before.loadedNodes, 2);
  assert.equal(before.loadedEdges, 1);
  assert.equal(before.getNode("2")?.type, "method");
  assert.equal(before.getNode("3"), null);

  const next = page([node(3)]);
  store.update({ ...input, nodePages: [first, next] });
  const after = store.getSnapshot();
  assert.equal(after.stream, before.stream);
  assert.deepEqual(
    after.data.nodes.map((value) => value.id),
    ["1", "3"],
  );
  assert.deepEqual(
    after.data.edges.map((value) => value.id),
    ["1|3"],
  );
  assert.equal(after.typeCounts.get("class"), 2);
  assert.equal(after.getNode("3")?.id, "3");
  assert.deepEqual(
    before.data.nodes.map((value) => value.id),
    ["1"],
  );
  assert.deepEqual(before.data.edges, []);
  assert.equal(before.typeCounts.get("class"), 1);
  assert.equal(
    before.getNode("3"),
    null,
    "Published lookup must not reveal future nodes",
  );
});

test("edges arriving before both endpoints activate exactly once, including self and external nodes", () => {
  const store = new GraphProjection();
  const edges = page([edge(1, 2), edge(2, 1), edge(2, 2), edge(2, 3)]);
  const first = page([node(1)]);
  const input = { nodePages: [], edgePages: [edges], types: ["*"], search: "" };
  store.update(input);
  assert.equal(store.getSnapshot().data.edges.length, 0);
  store.update({ ...input, nodePages: [first] });
  assert.equal(store.getSnapshot().data.edges.length, 0);
  const second = page([{ ...node(2), scope: "external" as const }]);
  store.update({ ...input, nodePages: [first, second] });
  assert.deepEqual(
    store
      .getSnapshot()
      .data.edges.map((value) => value.id)
      .sort(),
    ["1|2", "2|1", "2|2"],
  );
  const third = page([node(3)]);
  store.update({ ...input, nodePages: [first, second, third] });
  assert.equal(store.getSnapshot().data.edges.length, 4);
});

test("filters rebuild visibility and pending edges without mutating downloaded pages", () => {
  const store = new GraphProjection();
  const first = page([
    node(1, "class", "Client"),
    node(2, "method", "Client.Run"),
  ]);
  const second = page([node(3, "class", "Worker")]);
  const input = {
    nodePages: [first],
    edgePages: [page([edge(1, 2), edge(2, 3), edge(1, 3)])],
    types: ["class"],
    search: "",
  };
  store.update(input);
  store.update({ ...input, nodePages: [first, second] });
  assert.deepEqual(
    store.getSnapshot().data.edges.map((value) => value.id),
    ["1|3"],
  );
  const filteredStream = store.getSnapshot().stream;
  store.update({ ...input, nodePages: [first, second], types: ["*"] });
  assert.notEqual(store.getSnapshot().stream, filteredStream);
  assert.equal(store.getSnapshot().data.edges.length, 3);
  store.update({
    ...input,
    nodePages: [first, second],
    types: ["*"],
    search: "CLIENT",
  });
  assert.deepEqual(
    store.getSnapshot().data.nodes.map((value) => value.id),
    ["1", "2"],
  );
  assert.deepEqual(
    store.getSnapshot().data.edges.map((value) => value.id),
    ["1|2"],
  );
  assert.equal(store.getSnapshot().loadedNodes, 3);
  assert.equal(store.getSnapshot().typeCounts.get("class"), 2);
  assert.equal(first.items[1]?.type, "method");
});

test("refetched, reduced and revision-replaced pages reset append identity and stale lookups", () => {
  const store = new GraphProjection();
  const first = page([node(1, "class", "Old")]);
  const second = page([node(2)]);
  const input = {
    nodePages: [first, second],
    edgePages: [page([edge(1, 2)])],
    types: ["*"],
    search: "",
  };
  store.update(input);
  const previous = store.getSnapshot();
  store.update({
    ...input,
    nodePages: [page([node(1, "class", "New")], "r2")],
    edgePages: [],
  });
  const next = store.getSnapshot();
  assert.notEqual(next.stream, previous.stream);
  assert.equal(next.getNode("1")?.label, "New");
  assert.equal(next.getNode("2"), null);
  assert.equal(next.loadedEdges, 0);
  assert.equal(previous.getNode("1")?.label, "Old");
  assert.equal(previous.getNode("2")?.id, "2");
  const oldStream = next.stream;
  store.update({ ...input, nodePages: [], edgePages: [] });
  assert.notEqual(store.getSnapshot().stream, oldStream);
  assert.equal(store.getSnapshot().loadedNodes, 0);
});

test("edge-only pages preserve node snapshot identity; duplicate update publishes nothing", () => {
  const store = new GraphProjection();
  const nodes = [page([node(1), node(2)])];
  const input = {
    nodePages: nodes,
    edgePages: [],
    types: ["class"],
    search: "",
  };
  let notifications = 0;
  const unsubscribe = store.subscribe(() => notifications++);
  store.update(input);
  const before = store.getSnapshot();
  store.update(input);
  assert.equal(store.getSnapshot(), before);
  assert.equal(notifications, 1);
  store.update({ ...input, edgePages: [page([edge(1, 2)])] });
  assert.equal(store.getSnapshot().data.nodes, before.data.nodes);
  assert.equal(store.getSnapshot().typeCounts, before.typeCounts);
  assert.equal(store.getSnapshot().getNode, before.getNode);
  unsubscribe();
});

test("renderer appends skipped snapshots into reused capacity and preserves relaxed positions", () => {
  const projection = new GraphProjection();
  const buffers = new GraphBufferStore();
  const first = page([node(1), node(2)]);
  const input = { nodePages: [first], edgePages: [], types: ["*"], search: "" };
  projection.update(input);
  buffers.update(
    projection.getSnapshot().data,
    projection.getSnapshot().stream,
  );
  buffers.applyLayout(new Float32Array([1, 2, 3, 4, 5, 6]));
  const positionStorage = buffers.positions.buffer;
  const nodeIndex = buffers.buffers.indices;
  const second = page([node(3)]);
  projection.update({ ...input, nodePages: [first, second] });
  // Renderer skips this intermediate React snapshot.
  const third = page([node(4)]);
  projection.update({
    ...input,
    nodePages: [first, second, third],
    edgePages: [page([edge(1, 2), edge(2, 4)])],
  });
  const update = buffers.update(
    projection.getSnapshot().data,
    projection.getSnapshot().stream,
  );
  assert.equal(update.append, true);
  assert.equal(update.nodeStart, 2);
  assert.equal(buffers.positions.buffer, positionStorage);
  assert.equal(buffers.buffers.indices, nodeIndex);
  assert.deepEqual(
    [...buffers.buffers.positions.slice(0, 6)],
    [1, 2, 3, 4, 5, 6],
  );
  assert.equal(
    buffers.buffers.positions.length,
    12,
    "Only active positions reach picking and layout",
  );
  assert.deepEqual([...buffers.buffers.links], [0, 1, 1, 3]);
  const linkStorage = buffers.buffers.links.buffer;
  const snapshot = projection.getSnapshot();
  buffers.update(
    { nodes: snapshot.data.nodes, edges: [...snapshot.data.edges, edge(3, 4)] },
    snapshot.stream,
  );
  assert.equal(buffers.buffers.links.buffer, linkStorage);
  assert.deepEqual([...buffers.buffers.links], [0, 1, 1, 3, 2, 3]);
});

test("renderer capacity growth and filter resets retain active bounds without stale IDs or links", () => {
  const store = new GraphBufferStore();
  const stream = {};
  const nodes = Array.from({ length: 2050 }, (_, index) => node(index + 1));
  store.update({ nodes: nodes.slice(0, 2000), edges: [edge(1, 2)] }, stream);
  const storage = store.positions.buffer;
  store.buffers.positions.set([11, 12, 13]);
  store.update({ nodes, edges: [edge(1, 2), edge(1, 2050)] }, stream);
  assert.notEqual(store.positions.buffer, storage);
  assert.deepEqual([...store.buffers.positions.slice(0, 3)], [11, 12, 13]);
  assert.equal(store.buffers.positions.length, 6150);
  assert.equal(store.buffers.links.length, 4);
  store.update({ nodes: [nodes[2049], nodes[0]], edges: [edge(1, 2050)] }, {});
  assert.equal(store.buffers.positions.length, 6);
  assert.equal(store.buffers.indices.has("2"), false);
  assert.deepEqual([...store.buffers.positions.slice(3, 6)], [11, 12, 13]);
  assert.deepEqual([...store.buffers.links], [1, 0]);
  assert.equal(
    store.applyLayout(new Float32Array(6150)),
    false,
    "Stale-size layout cannot replace active positions",
  );
  store.update({ nodes: [], edges: [] }, {});
  assert.equal(store.buffers.positions.length, 0);
  assert.equal(store.buffers.links.length, 0);
});
