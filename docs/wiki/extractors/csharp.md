---
title: "CSharp"
type: extractor
tags: [csharp, roslyn, implemented]
created: 2026-04-26
updated: 2026-04-28
confidence: high
---

## Target Language

C#. `CSharpLanguageExtractor` is the Roslyn-backed extractor that turns a Target into project nodes, code nodes, dependency edges, and diagnostics while keeping MSBuild loading separate from in-memory analysis.

## Full Index Logic

1. `CSharpLanguageExtractor.Extract()` sends the Target path, a fresh `RoslynWorkspaceOptions`, and the optional progress reporter to `IWorkspaceLoader`.
2. `IWorkspaceLoader` opens or reuses the MSBuild-backed workspace snapshot for the Target, using the [[architecture/virtual-file-system]] seam for path and file checks.
3. `CSharpLanguageExtractor` then hands the already-loaded `Solution` plus repository workspace details to `ITargetAnalysisEngine`.
4. `ITargetAnalysisEngine` creates repository-relative project ids and project nodes for the ordered projects in the workspace snapshot.
5. `NodeExtractor` walks the ordered projects and documents to emit `CodeNode` records plus the symbol maps needed for edge construction.
6. `EdgeExtractor` consumes the same Roslyn snapshot and symbol maps to emit dependency edges.
7. The extractor returns projects, code nodes, edges, and diagnostics to the indexing pipeline described in [[persistence/sqlite-schema]].

## Incremental Logic

1. `ExtractIncremental()` filters the incoming batch to C#-affected changes only.
2. `IWorkspaceLoader.Load()` ensures the Target is present in the workspace cache, and `IWorkspaceLoader.UpdateDocuments()` applies modified-document text updates in place when possible.
3. Any add, delete, rename, or unresolved file forces a workspace reload so Roslyn state never drifts from the real Target.
4. `ITargetAnalysisEngine.ExtractIncremental()` analyzes only the changed documents from the updated in-memory `Solution`.
5. The resulting nodes and edges flow through the RelativeFilePath-targeted overwrite path coordinated by [[architecture/incremental-watch]].
6. Because the analysis engine accepts in-memory `Solution` and `Project` models directly, the default Roslyn tests can exercise extraction through `AdhocWorkspace` instead of temp directories.

## Dependencies

- Roslyn `MSBuildWorkspace` and `Microsoft.Build.Locator` for loading C# Targets.
- `IWorkspaceLoader`, `WorkspaceLoader`, `MsBuildWorkspaceFactory`, and `MsBuildLocatorRegistration` for workspace lifecycle management.
- `ITargetAnalysisEngine`, `NodeExtractor`, and `EdgeExtractor` for syntax and semantic extraction over already-loaded Roslyn models.
- `IRepositoryWorkspace` for repository-relative project and document paths.
- [[architecture/virtual-file-system]] for filesystem-backed path and content access.
