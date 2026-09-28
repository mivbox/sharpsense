export type WorkspaceOverview = {
  id: string | null;
  name: string;
  root: string;
  sources: { kind: string; path: string }[];
  projects: number;
  files: number;
  nodes: number;
  edges: number;
  memories: number;
  isIndexed: boolean;
};
export type TreeNode = {
  id: number;
  parentId: number | null;
  path: string;
  label: string;
  kind: "project" | "folder" | "file";
  hasChildren: boolean;
  childCount: number | null;
  isSelectable: boolean;
};
export type TreeResult = {
  parentPath: string;
  parentDirectoryId: number | null;
  nodes: TreeNode[];
};
export type GraphNodeSummary = {
  id: string;
  codeNodeId: number | null;
  label: string;
  type: string;
  relativePath: string | null;
  projectId: string | null;
};
export type GraphNode = GraphNodeSummary & {
  scope: "selected" | "external";
  isClickable: boolean;
};
export type GraphEdge = {
  id: string;
  source: string;
  target: string;
  type: string;
  scope: "internal" | "boundary";
  metadata?: string | null;
};
export type GraphData = {
  readonly nodes: readonly GraphNode[];
  readonly edges: readonly GraphEdge[];
};
export type GraphPage<T> = {
  items: T[];
  revision: string;
  nextCursor: string | null;
  totalCount: number | null;
};
export type GraphRelationship = {
  type: string;
  direction: "incoming" | "outgoing" | "self";
  metadata: string | null;
};
export type GraphNodeConnection = {
  node: GraphNodeSummary;
  relationships: GraphRelationship[];
};
export type GraphNodeConnectionsPage = GraphPage<GraphNodeConnection> & {
  node: GraphNodeSummary;
};
export type MemoryIntent =
  "Convention" | "Invariant" | "Todo" | "Warning" | "Decision";
export const memoryIntents: MemoryIntent[] = [
  "Convention",
  "Invariant",
  "Todo",
  "Warning",
  "Decision",
];
export type Memory = {
  id: string;
  targetFullyQualifiedName: string;
  content?: string;
  intent: MemoryIntent;
  tags: string[];
  isStale: boolean;
  createdAt?: string;
};
export type SearchHit = {
  nodeId: number;
  label: string;
  kind: string;
  path: string;
  summary: string;
  startLine?: number;
  endLine?: number;
};
export type ToolId =
  "context" | "trace" | "inheritors" | "impact" | "graph_stats";
export type ToolInput = {
  nodeId?: number;
  direction?: "caller" | "callee";
  maxDepth?: number;
  maxRelated?: number;
};
export type ToolSelection = {
  tool: ToolId;
  nodeId: number | null;
  label?: string;
};
