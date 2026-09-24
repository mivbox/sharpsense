import type {
  GraphData,
  GraphEdge,
  GraphNode,
  GraphPage,
} from "../../shared/api/models";

export type GraphProjectionInput = {
  nodePages: readonly GraphPage<GraphNode>[];
  edgePages: readonly GraphPage<GraphEdge>[];
  types: readonly string[];
  search: string;
};

export type GraphProjectionSnapshot = {
  data: GraphData;
  /** Same token guarantees append-only visible arrays, even across skipped renders. */
  stream: object;
  loadedNodes: number;
  loadedEdges: number;
  typeCounts: ReadonlyMap<string, number>;
  getNode: (id: string) => GraphNode | null;
};

function filterKey(types: readonly string[], search: string) {
  return JSON.stringify([[...new Set(types)].sort(), search.toLowerCase()]);
}

function extendsPages<T>(
  previous: readonly GraphPage<T>[],
  next: readonly GraphPage<T>[],
) {
  return (
    previous.length <= next.length &&
    previous.every((page, index) => page === next[index])
  );
}

/**
 * Owns preparation indexes, never query pages. Updates run after React commits;
 * published arrays, counts and lookup results remain unchanged for old readers.
 */
export class GraphProjection {
  private nodePages: readonly GraphPage<GraphNode>[] = [];
  private edgePages: readonly GraphPage<GraphEdge>[] = [];
  private key = "";
  private nodeIndex = new Map<string, { node: GraphNode; ordinal: number }>();
  private visibleIds = new Set<string>();
  private counts = new Map<string, number>();
  private waiting = new Map<string, GraphEdge[]>();
  private listeners = new Set<() => void>();
  private snapshot: GraphProjectionSnapshot = this.emptySnapshot();

  getSnapshot = () => this.snapshot;

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  private emptySnapshot(): GraphProjectionSnapshot {
    return {
      data: { nodes: [], edges: [] },
      stream: {},
      loadedNodes: 0,
      loadedEdges: 0,
      typeCounts: new Map(),
      getNode: () => null,
    };
  }

  update(input: GraphProjectionInput) {
    const key = filterKey(input.types, input.search);
    if (
      input.nodePages === this.nodePages &&
      input.edgePages === this.edgePages &&
      key === this.key
    )
      return;
    const reset =
      key !== this.key ||
      !extendsPages(this.nodePages, input.nodePages) ||
      !extendsPages(this.edgePages, input.edgePages);
    if (reset) {
      this.nodePages = [];
      this.edgePages = [];
      this.nodeIndex = new Map();
      this.visibleIds = new Set();
      this.counts = new Map();
      this.waiting = new Map();
      this.snapshot = this.emptySnapshot();
    }
    this.key = key;
    const previous = this.snapshot;
    const addedNodes: GraphNode[] = [];
    const addedEdges: GraphEdge[] = [];
    const awakened: GraphEdge[] = [];
    const types = new Set(input.types);
    const allTypes = types.has("*");
    const search = input.search.toLowerCase();
    let loadedNodes = previous.loadedNodes;
    let loadedEdges = previous.loadedEdges;

    for (
      let pageIndex = this.nodePages.length;
      pageIndex < input.nodePages.length;
      pageIndex++
    ) {
      for (const node of input.nodePages[pageIndex].items) {
        // Repeated IDs cannot replace values observed by an earlier snapshot.
        if (this.nodeIndex.has(node.id)) continue;
        this.nodeIndex.set(node.id, { node, ordinal: loadedNodes++ });
        this.counts.set(node.type, (this.counts.get(node.type) ?? 0) + 1);
        if (
          (allTypes || types.has(node.type)) &&
          (!search ||
            node.label.toLowerCase().includes(search) ||
            node.type.toLowerCase().includes(search) ||
            node.relativePath?.toLowerCase().includes(search) ||
            String(node.codeNodeId).includes(search))
        ) {
          addedNodes.push(node);
          this.visibleIds.add(node.id);
        }
        const waiting = this.waiting.get(node.id);
        if (waiting) {
          for (const edge of waiting) awakened.push(edge);
          this.waiting.delete(node.id);
        }
      }
    }

    for (const edge of awakened) {
      this.includeEdge(edge, addedEdges);
    }

    for (
      let pageIndex = this.edgePages.length;
      pageIndex < input.edgePages.length;
      pageIndex++
    ) {
      for (const edge of input.edgePages[pageIndex].items) {
        loadedEdges++;
        this.includeEdge(edge, addedEdges);
      }
    }

    this.nodePages = input.nodePages;
    this.edgePages = input.edgePages;
    const nodes = addedNodes.length
      ? previous.data.nodes.length
        ? previous.data.nodes.concat(addedNodes)
        : addedNodes
      : previous.data.nodes;
    const edges = addedEdges.length
      ? previous.data.edges.length
        ? previous.data.edges.concat(addedEdges)
        : addedEdges
      : previous.data.edges;
    const nodeIndex = this.nodeIndex;
    this.snapshot = {
      data:
        nodes === previous.data.nodes && edges === previous.data.edges
          ? previous.data
          : { nodes, edges },
      stream: previous.stream,
      loadedNodes,
      loadedEdges,
      typeCounts:
        loadedNodes === previous.loadedNodes
          ? previous.typeCounts
          : new Map(this.counts),
      getNode:
        loadedNodes === previous.loadedNodes
          ? previous.getNode
          : (id) => {
              const value = nodeIndex.get(id);
              return value && value.ordinal < loadedNodes ? value.node : null;
            },
    };
    for (const listener of this.listeners) listener();
  }

  private includeEdge(edge: GraphEdge, added: GraphEdge[]) {
    const sourceKnown = this.nodeIndex.has(edge.source);
    const targetKnown = this.nodeIndex.has(edge.target);
    if (
      (sourceKnown && !this.visibleIds.has(edge.source)) ||
      (targetKnown && !this.visibleIds.has(edge.target))
    )
      return;
    if (sourceKnown && targetKnown) {
      added.push(edge);
      return;
    }
    // One queue membership per unresolved edge, even when both endpoints are
    // absent. Arrival either activates it or moves it to its other endpoint.
    const missingId = sourceKnown ? edge.target : edge.source;
    let waiting = this.waiting.get(missingId);
    if (!waiting) {
      waiting = [];
      this.waiting.set(missingId, waiting);
    }
    waiting.push(edge);
  }
}
