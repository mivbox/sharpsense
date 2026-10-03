import type { GraphEdge } from "../../shared/api/models";

export function edgeLabel(edge: Pick<GraphEdge, "type" | "metadata">): string {
  const label =
    edge.type === "http-request"
      ? "HTTP request"
      : edge.type === "import"
        ? "Imports"
        : edge.type;

  return edge.metadata ? `${label} · ${edge.metadata}` : label;
}
