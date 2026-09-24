export type SourceKind = "CSharp" | "TypeScript" | "Markdown";
export type WorkspaceSource = { kind: SourceKind; path: string };
export type WorkspaceSummary = {
  id: string;
  name: string;
  repositoryRoot: string;
  sources: WorkspaceSource[];
};
export type WorkspaceInput = Omit<WorkspaceSummary, "id">;
export const sourceKinds: SourceKind[] = ["CSharp", "TypeScript", "Markdown"];
export const sourceLabels: Record<SourceKind, string> = {
  CSharp: "C# project or solution",
  TypeScript: "TypeScript config or directory",
  Markdown: "Documentation glob",
};
