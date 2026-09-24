import type { GraphEdge } from "../../shared/api/models";

export const nodeColors: Record<string, string> = {
  project: "#a6b3ff",
  class: "#80cbc4",
  interface: "#ce93d8",
  method: "#90caf9",
  property: "#ffcc80",
  field: "#ef9a9a",
  document: "#b0bec5",
  namespace: "#9fa8da",
  component: "#a5d6a7",
  enum: "#ffe082",
  struct: "#80deea",
  record: "#80cbc4",
  http: "#ffab91",
  package: "#bcaaa4",
};

export function edgeLabel(edge: Pick<GraphEdge, "type" | "metadata">): string {
  const label =
    edge.type === "http-request"
      ? "HTTP request"
      : edge.type === "import"
        ? "Imports"
        : edge.type;

  return edge.metadata ? `${label} · ${edge.metadata}` : label;
}
