import type { ToolId } from "../shared/api/models";

export type ExplorerSearch = {
  scopes: string[];
  selected?: string;
  mode: "graph" | "list";
  types: string[];
  edges: boolean;
  filter: string;
};
export type WorkspaceSearch = { q: string; limit: number };
export type ToolsSearch = {
  tool: ToolId;
  nodeId?: number;
  direction: "caller" | "callee";
  depth: number;
  maxRelated: number;
  label?: string;
};
export const explorerDefaults: ExplorerSearch = {
  scopes: [],
  mode: "graph",
  types: [
    "project",
    "class",
    "interface",
    "component",
    "document",
    "http",
    "package",
  ],
  edges: false,
  filter: "",
};
export const searchDefaults: WorkspaceSearch = { q: "", limit: 20 };
export const toolsDefaults: ToolsSearch = {
  tool: "context",
  direction: "callee",
  depth: 3,
  maxRelated: 10,
};
function integer(value: unknown, fallback: number, min: number, max: number) {
  if (typeof value !== "string" && typeof value !== "number") return fallback;
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= min && parsed <= max
    ? parsed
    : fallback;
}
export function validateExplorerSearch(
  value: Record<string, unknown>,
): ExplorerSearch {
  return {
    scopes: Array.isArray(value.scopes)
      ? [
          ...new Set(
            value.scopes.filter(
              (path): path is string =>
                typeof path === "string" && path.length > 0,
            ),
          ),
        ].slice(0, 100)
      : [],
    selected: typeof value.selected === "string" ? value.selected : undefined,
    mode: value.mode === "list" ? "list" : "graph",
    types: Array.isArray(value.types)
      ? [
          ...new Set(
            value.types.filter(
              (type): type is string =>
                typeof type === "string" &&
                (type === "*" || /^[a-z][a-z0-9-]{0,31}$/.test(type)),
            ),
          ),
        ].slice(0, 100)
      : explorerDefaults.types,
    edges: value.edges === true,
    filter: typeof value.filter === "string" ? value.filter : "",
  };
}
export function validateWorkspaceSearch(
  value: Record<string, unknown>,
): WorkspaceSearch {
  return {
    q: typeof value.q === "string" ? value.q : "",
    limit: [10, 20, 50].includes(Number(value.limit))
      ? Number(value.limit)
      : 20,
  };
}
export function validateToolsSearch(
  value: Record<string, unknown>,
): ToolsSearch {
  const nodeId = integer(value.nodeId, 0, 1, 2147483647);
  return {
    tool: ["context", "trace", "inheritors", "impact", "graph_stats"].includes(
      String(value.tool),
    )
      ? (value.tool as ToolId)
      : "context",
    nodeId: nodeId || undefined,
    direction: value.direction === "caller" ? "caller" : "callee",
    depth: integer(value.depth, 3, 1, 10),
    maxRelated: [5, 10, 20, 50].includes(Number(value.maxRelated))
      ? Number(value.maxRelated)
      : 10,
    label: typeof value.label === "string" ? value.label : undefined,
  };
}
