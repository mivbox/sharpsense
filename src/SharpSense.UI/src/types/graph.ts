export type GraphNodeScope = "selected" | "external";
export type GraphEdgeScope = "internal" | "boundary";

export type GraphApiNode = {
  id: string;
  label: string;
  type: string;
  relativePath: string | null;
  projectId: string | null;
  scope: GraphNodeScope;
  isClickable: boolean;
};

export type GraphApiEdge = {
  id: string;
  source: string;
  target: string;
  type: string;
  scope: GraphEdgeScope;
};

export type GraphApiResponse = {
  nodes: GraphApiNode[];
  edges: GraphApiEdge[];
};

export const EMPTY_GRAPH: GraphApiResponse = {
  nodes: [],
  edges: []
};
