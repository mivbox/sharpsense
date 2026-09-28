/**
 * Deterministic preparation-only benchmark; no browser, GPU, network or worker.
 * Run with pnpm exec node --expose-gc --import tsx tests/graphPreparation.profile.ts
 * Add "baseline" for previous pipeline and "all" for all node types.
 */
import assert from "node:assert/strict";
import { GraphProjection } from "../src/features/explorer/graphProjection";
import { GraphBufferStore } from "../src/features/explorer/rendering/GraphBufferStore";
import {
  prepareGraphBuffers,
  prepareGraphLinks,
} from "../src/features/explorer/rendering/graphBuffers";
import type {
  GraphData,
  GraphEdge,
  GraphNode,
  GraphPage,
} from "../src/shared/api/models";

const baseline = process.argv.includes("baseline");
const all = process.argv.includes("all");
const nodeCount = 212_849;
const edgeCount = 682_838;
const types = all ? ["*"] : ["project", "class", "interface"];
const nodes: GraphNode[] = Array.from({ length: nodeCount }, (_, index) => ({
  id: String(index + 1),
  codeNodeId: index + 1,
  label: `Company.Project${index % 338}.Namespace.Symbol${index}`,
  type:
    index % 629 === 0
      ? "project"
      : index % 107 === 0
        ? "interface"
        : index % 10 === 0
          ? "class"
          : "method",
  relativePath: `src/Project${index % 338}/Namespace/File${Math.floor(index / 10)}.cs`,
  projectId: String(index % 338),
  scope: "selected",
  isClickable: true,
}));
const edges: GraphEdge[] = Array.from({ length: edgeCount }, (_, index) => {
  const source = index % nodeCount;
  const target =
    (source * 37 + Math.floor(index / nodeCount) * 7919 + 1) % nodeCount;
  return {
    id: `${source + 1}|${target + 1}|methodcall`,
    source: String(source + 1),
    target: String(target + 1),
    type: "methodcall",
    scope: "internal",
  };
});
function pages<T>(items: T[], firstSize = 5000): GraphPage<T>[] {
  const result: GraphPage<T>[] = [];
  for (let offset = 0; offset < items.length;) {
    const size = offset === 0 ? firstSize : 5000;
    result.push({
      items: items.slice(offset, offset + size),
      revision: "fixture",
      nextCursor: null,
      totalCount: items.length,
    });
    offset += size;
  }
  return result;
}
const nodePages = pages(nodes, 2000);
const edgePages = pages(edges);
const projection = new GraphProjection();
const renderer = new GraphBufferStore();
let oldBuffers = prepareGraphBuffers({ nodes: [], edges: [] });
let current: GraphData = { nodes: [], edges: [] };
let loadedNodes: GraphNode[];
let loadedEdges: GraphEdge[] = [];
let filtered: readonly GraphNode[] = [];
let seenNodes: GraphPage<GraphNode>[] = [];
let seenEdges: GraphPage<GraphEdge>[] = [];
let referenceSlots = 0;
let typedAllocationBytes = 0;
let positionsBuffer: ArrayBufferLike | undefined;
let linksBuffer: ArrayBufferLike | undefined;
const timings: number[] = [];
const retainedHeapMiB: { step: number; delta: number }[] = [];

global.gc?.();
const initialHeap = process.memoryUsage().heapUsed;
const steps = edgePages.length + 1;
for (let step = 0; step < steps; step++) {
  const nodeEnd = Math.min(nodePages.length, 1 + Math.floor(step / 3));
  const nextNodes =
    nodeEnd === seenNodes.length ? seenNodes : nodePages.slice(0, nodeEnd);
  const nextEdges = step === 0 ? seenEdges : edgePages.slice(0, step);
  const started = performance.now();

  if (baseline) {
    if (nextNodes !== seenNodes) {
      loadedNodes = nextNodes.flatMap((page) => page.items);
      referenceSlots += loadedNodes.length;
      filtered = loadedNodes.filter((node) => all || types.includes(node.type));
      referenceSlots += filtered.length;
      // Previous explorer counted every node type on each node page.
      const counts = new Map<string, number>();
      for (const node of loadedNodes)
        counts.set(node.type, (counts.get(node.type) ?? 0) + 1);
    }
    if (nextEdges !== seenEdges) {
      loadedEdges = nextEdges.flatMap((page) => page.items);
      referenceSlots += loadedEdges.length;
    }
    const ids = new Set(filtered.map((node) => node.id));
    referenceSlots += filtered.length;
    const visible = loadedEdges.filter(
      (edge) => ids.has(edge.source) && ids.has(edge.target),
    );
    referenceSlots += visible.length;
    const next = { nodes: filtered, edges: visible };
    if (next.nodes !== oldBuffers.nodes) {
      oldBuffers = prepareGraphBuffers(next, oldBuffers);
    } else {
      const append =
        current.edges.length <= visible.length &&
        current.edges.every((edge, index) => edge === visible[index]);
      const added = prepareGraphLinks(
        append ? visible.slice(current.edges.length) : visible,
        oldBuffers.indices,
      );
      const links = new Uint32Array(
        (append ? oldBuffers.links.length : 0) + added.length,
      );
      if (append) links.set(oldBuffers.links);
      links.set(added, append ? oldBuffers.links.length : 0);
      oldBuffers.links = links;
      typedAllocationBytes += added.byteLength;
    }
    current = next;
    if (positionsBuffer !== oldBuffers.positions.buffer) {
      positionsBuffer = oldBuffers.positions.buffer;
      typedAllocationBytes += positionsBuffer.byteLength;
    }
    if (linksBuffer !== oldBuffers.links.buffer) {
      linksBuffer = oldBuffers.links.buffer;
      typedAllocationBytes += linksBuffer.byteLength;
    }
  } else {
    projection.update({
      nodePages: nextNodes,
      edgePages: nextEdges,
      types,
      search: "",
    });
    const snapshot = projection.getSnapshot();
    if (snapshot.data.nodes !== current.nodes)
      referenceSlots += snapshot.data.nodes.length;
    if (snapshot.data.edges !== current.edges)
      referenceSlots += snapshot.data.edges.length;
    current = snapshot.data;
    renderer.update(snapshot.data, snapshot.stream);
    if (positionsBuffer !== renderer.positions.buffer) {
      positionsBuffer = renderer.positions.buffer;
      typedAllocationBytes += positionsBuffer.byteLength;
    }
    if (linksBuffer !== renderer.buffers.links.buffer) {
      linksBuffer = renderer.buffers.links.buffer;
      typedAllocationBytes += linksBuffer.byteLength;
    }
  }

  timings.push(performance.now() - started);
  seenNodes = nextNodes;
  seenEdges = nextEdges;
  if (
    step === Math.floor(steps / 4) ||
    step === Math.floor(steps / 2) ||
    step === steps - 1
  ) {
    global.gc?.();
    retainedHeapMiB.push({
      step,
      delta: Number(
        ((process.memoryUsage().heapUsed - initialHeap) / 1024 ** 2).toFixed(2),
      ),
    });
  }
}

const expectedIds = new Set(
  nodes
    .filter((node) => all || types.includes(node.type))
    .map((node) => node.id),
);
const expectedEdges = edges.filter(
  (edge) => expectedIds.has(edge.source) && expectedIds.has(edge.target),
);
assert.equal(current.nodes.length, expectedIds.size);
assert.equal(current.edges.length, expectedEdges.length);
assert.deepEqual(
  new Set(current.edges.map((edge) => edge.id)),
  new Set(expectedEdges.map((edge) => edge.id)),
);
if (!baseline) {
  assert.equal(projection.getSnapshot().loadedNodes, nodeCount);
  assert.equal(projection.getSnapshot().loadedEdges, edgeCount);
  assert.equal(renderer.buffers.positions.length, current.nodes.length * 3);
  assert.equal(renderer.buffers.links.length, current.edges.length * 2);
}
const sorted = [...timings].sort((a, b) => a - b);
console.log(
  JSON.stringify({
    pipeline: baseline ? "previous" : "incremental",
    types: all ? "all" : "default",
    input: { nodes: nodeCount, edges: edgeCount, updates: timings.length },
    visible: { nodes: current.nodes.length, edges: current.edges.length },
    preparationMs: Number(
      timings.reduce((sum, value) => sum + value, 0).toFixed(2),
    ),
    p95UpdateMs: Number(sorted[Math.floor(sorted.length * 0.95)].toFixed(2)),
    maxUpdateMs: Number(Math.max(...timings).toFixed(2)),
    materializedArrayReferenceSlots: referenceSlots,
    rendererBackingAllocationMiB: Number(
      (typedAllocationBytes / 1024 ** 2).toFixed(2),
    ),
    retainedHeapMiB,
    gcAvailable: Boolean(global.gc),
    excludes:
      "GPU rendering, worker layout/transfers, network; array slots exclude Set/Map entries and temporary per-page arrays",
  }),
);
