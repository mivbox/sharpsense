import type {
  SourceKind,
  WorkspaceSummary,
} from "../../shared/workspace/models";

export type WorkspaceInput = Omit<WorkspaceSummary, "id">;
export const sourceKinds: SourceKind[] = ["CSharp", "TypeScript", "Markdown"];
export const sourceLabels: Record<SourceKind, string> = {
  CSharp: "C# project or solution",
  TypeScript: "TypeScript config or directory",
  Markdown: "Documentation glob",
};
