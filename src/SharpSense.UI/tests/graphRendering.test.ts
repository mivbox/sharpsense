import assert from "node:assert/strict";
import test from "node:test";
import { Matrix4, PerspectiveCamera } from "three";
import { LayoutQueue } from "../src/features/explorer/rendering/layoutQueue";
import type { LayoutRequest } from "../src/features/explorer/rendering/layoutProtocol";
import type { GraphData, GraphNode } from "../src/shared/api/models";
import {
  pickGraphNode,
  prepareGraphBuffers,
  writeEdgePositions,
} from "../src/features/explorer/rendering/graphBuffers";
import {
  createGraphLayout,
  layoutPositions,
} from "../src/features/explorer/rendering/graphLayout";

function node(id: number): GraphNode {
  return {
    id: String(id),
    codeNodeId: id,
    label: `Node${id}`,
    type: "class",
    relativePath: "src/Graph.cs",
    projectId: "project",
    scope: "selected",
    isClickable: true,
  };
}

test("progressive append and ordering changes preserve relaxed positions by node ID", () => {
  const previous = prepareGraphBuffers({
    nodes: [node(1), node(2)],
    edges: [],
  });
  previous.positions.set([10, 20, 30, 40, 50, 60]);
  const next = prepareGraphBuffers(
    { nodes: [node(2), node(3), node(1)], edges: [] },
    previous,
  );
  assert.deepEqual([...next.positions.slice(0, 3)], [40, 50, 60]);
  assert.deepEqual([...next.positions.slice(6, 9)], [10, 20, 30]);
  assert.deepEqual(
    [...next.positions.slice(3, 6)],
    [...prepareGraphBuffers({ nodes: [node(3)], edges: [] }).positions],
  );
});

test("visible graph links omit missing endpoints without dropping loaded nodes", () => {
  const data: GraphData = {
    nodes: [node(1), node(2)],
    edges: [
      {
        id: "1",
        source: "1",
        target: "2",
        type: "MethodCall",
        scope: "internal",
      },
      {
        id: "2",
        source: "1",
        target: "3",
        type: "MethodCall",
        scope: "boundary",
      },
    ],
  };
  const buffers = prepareGraphBuffers(data);
  assert.deepEqual([...buffers.links], [0, 1]);
  const edges = new Float32Array(6);
  writeEdgePositions(
    edges,
    new Float32Array([10, 20, 30, 40, 50, 60]),
    buffers.links,
  );
  assert.deepEqual([...edges], [10, 20, 30, 40, 50, 60]);
});

test("batched buffers retain every node beyond previous rendering limits", () => {
  const nodes = Array.from({ length: 50001 }, (_, index) => node(index + 1));
  const buffers = prepareGraphBuffers({ nodes, edges: [] });
  assert.equal(buffers.nodes.length, 50001);
  assert.equal(buffers.positions.length, 150003);
  assert.equal(buffers.indices.get("50001"), 50000);
  assert.ok(buffers.positions.every(Number.isFinite));
});

test("pixel picking selects nearest visible node and rejects empty space", () => {
  const positions = new Float32Array([0, 0, 0.5, 0, 0, -0.5, 0.8, 0.8, 0]);
  const matrix = new Matrix4().elements;
  assert.equal(pickGraphNode(positions, matrix, 200, 100, 100, 50, 8), 1);
  assert.equal(pickGraphNode(positions, matrix, 200, 100, 180, 10, 8), 2);
  assert.equal(pickGraphNode(positions, matrix, 200, 100, 10, 90, 8), null);
});

test("pixel picking ignores points behind camera or outside clipping planes", () => {
  const positions = new Float32Array([0, 0, 2]);
  const matrix = new Matrix4();
  assert.equal(
    pickGraphNode(positions, matrix.elements, 200, 100, 100, 50, 8),
    null,
  );
  matrix.elements[15] = -1;
  assert.equal(
    pickGraphNode(
      new Float32Array([0, 0, 0]),
      matrix.elements,
      200,
      100,
      100,
      50,
      8,
    ),
    null,
  );
});

test("pixel picking includes visible project-node edges across zoom and pixel ratios", () => {
  const camera = new PerspectiveCamera(50, 2, 0.1, 10_000);
  const width = 800;
  const height = 400;
  for (const pixelRatio of [1, 2]) {
    const sizing = {
      sizes: new Float32Array([16]),
      scale:
        (height * pixelRatio) / (2 * Math.tan((camera.fov * Math.PI) / 360)),
      minimumSize: 10 * pixelRatio,
      maximumSize: 28 * pixelRatio,
      pixelRatio,
    };
    // At either distance the visible sprite reaches its 28 CSS-pixel cap.
    for (const distance of [30, 100]) {
      const positions = new Float32Array([0, 0, -distance]);
      assert.equal(
        pickGraphNode(
          positions,
          camera.projectionMatrix.elements,
          width,
          height,
          413,
          200,
          10,
          sizing,
        ),
        0,
        `Visible outer pixels must select at depth ${distance}, DPR ${pixelRatio}`,
      );
      assert.equal(
        pickGraphNode(
          positions,
          camera.projectionMatrix.elements,
          width,
          height,
          415,
          200,
          10,
          sizing,
        ),
        null,
        "Pixels outside sprite and minimum tolerance must remain empty",
      );
    }
    const distant = new Float32Array([0, 0, -1000]);
    assert.equal(
      pickGraphNode(
        distant,
        camera.projectionMatrix.elements,
        width,
        height,
        409,
        200,
        10,
        sizing,
      ),
      0,
      "Small distant nodes retain usable minimum hit target",
    );
    assert.equal(
      pickGraphNode(
        distant,
        camera.projectionMatrix.elements,
        width,
        height,
        413,
        200,
        10,
        sizing,
      ),
      null,
      "Zooming out must shrink hit area alongside visible sprite",
    );
    assert.equal(
      pickGraphNode(
        distant,
        camera.projectionMatrix.elements,
        width,
        height,
        411,
        200,
        2,
        { ...sizing, minimumSize: 24 * pixelRatio },
      ),
      0,
      "Shader minimum size must also remain fully clickable",
    );
  }
});

test("worker layout keeps finite positions for cycles and disconnected nodes", () => {
  const request = {
    revision: 1,
    ids: ["1", "2", "3", "4"],
    positions: new Float32Array([0, 0, 0, 10, 20, 30, -20, 10, 20, 50, 60, 70]),
    links: new Uint32Array([0, 1, 1, 2, 2, 0]),
  };
  const layout = createGraphLayout(request);
  layout.simulation.tick(10);
  assert.ok(layoutPositions(layout.nodes).every(Number.isFinite));
  const appended = createGraphLayout(
    {
      ...request,
      revision: 2,
      ids: [...request.ids, "5"],
      positions: new Float32Array([...request.positions, 90, 90, 90]),
    },
    layout.nodes,
  );
  assert.equal(appended.nodes[0], layout.nodes[0]);
  assert.equal(appended.nodes.length, 5);
  appended.simulation.tick();
  assert.ok(layoutPositions(appended.nodes).every(Number.isFinite));
});

test("layout backpressure constructs only the latest pending graph snapshot", () => {
  const sent: LayoutRequest[] = [];
  const built: number[] = [];
  const queue = new LayoutQueue((request) => sent.push(request));
  const create = (revision: number): LayoutRequest => {
    built.push(revision);
    return {
      revision,
      ids: [String(revision)],
      positions: new Float32Array(3),
      links: new Uint32Array(),
    };
  };
  queue.enqueue(1, create);
  for (let revision = 2; revision <= 100; revision++)
    queue.enqueue(revision, create);
  assert.deepEqual(built, [1]);
  queue.acknowledge(1);
  assert.deepEqual(built, [1, 100]);
  assert.deepEqual(
    sent.map((request) => request.revision),
    [1, 100],
  );
  queue.enqueue(101, create);
  queue.acknowledge(1);
  assert.deepEqual(
    built,
    [1, 100],
    "Stale snapshots must not release current worker capacity.",
  );
  queue.acknowledge(100);
  assert.deepEqual(built, [1, 100, 101]);
});

test("empty and failed layouts release capacity; disposed scopes cannot send pending work", () => {
  const sent: number[] = [];
  const queue = new LayoutQueue((request) => sent.push(request.revision));
  const create = (revision: number): LayoutRequest => ({
    revision,
    ids: [],
    positions: new Float32Array(),
    links: new Uint32Array(),
  });
  queue.enqueue(1, create);
  queue.enqueue(2, create);
  queue.acknowledge(1);
  assert.deepEqual(sent, [1, 2]);
  queue.enqueue(3, create);
  // Both snapshots and error messages acknowledge their revision before handling content.
  queue.acknowledge(2);
  assert.deepEqual(sent, [1, 2, 3]);
  queue.enqueue(4, create);
  queue.dispose();
  queue.acknowledge(3);
  queue.enqueue(5, create);
  assert.deepEqual(sent, [1, 2, 3]);
});

test("failed worker dispatch does not leave its queue permanently occupied", () => {
  let attempts = 0;
  const queue = new LayoutQueue(() => {
    if (++attempts === 1) throw new Error("Worker unavailable");
  });
  const create = (revision: number): LayoutRequest => ({
    revision,
    ids: [],
    positions: new Float32Array(),
    links: new Uint32Array(),
  });
  assert.throws(() => queue.enqueue(1, create), /Worker unavailable/);
  assert.doesNotThrow(() => queue.enqueue(2, create));
  assert.equal(attempts, 2);
});
