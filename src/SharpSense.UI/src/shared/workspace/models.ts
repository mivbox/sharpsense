export type SourceKind = "CSharp" | "TypeScript" | "Markdown";
export type WorkspaceSource = { kind: SourceKind; path: string };
export type WorkspaceSummary = {
  id: string;
  name: string;
  repositoryRoot: string;
  sources: WorkspaceSource[];
};
