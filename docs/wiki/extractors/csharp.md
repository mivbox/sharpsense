---
title: "CSharp"
type: extractor
tags: [csharp, roslyn, implemented]
created: 2026-04-26
updated: 2026-05-06
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
6. For C# declarations, `RoslynSymbolUtilities` now extracts only `<summary>` and `<remarks>` text, strips nested XML formatting, ignores low-signal tags such as `<param>`, `<returns>`, and `<exception>`, and stores that intent text separately from the rendered signature.
7. `NodeExtractor` also computes a one-way SHA-256 hash for method bodies only. The raw method-body text never leaves the Roslyn pass; only the hash is carried forward into persistence.
8. `EdgeExtractor` consumes the same Roslyn snapshot and symbol maps to emit dependency edges.
9. The extractor returns projects, code nodes, edges, and diagnostics to the indexing pipeline described in [[persistence/sqlite-schema]].

## Incremental Logic

1. `ExtractIncremental()` filters the incoming batch to C#-affected changes only.
2. `IWorkspaceLoader.Load()` ensures the Target is present in the workspace cache, and `IWorkspaceLoader.UpdateDocuments()` applies modified-document text updates in place when possible.
3. Modified-document reads now retry across short transient failures; if the file still cannot be read stably, the loader falls back to reloading the Roslyn workspace instead of failing the whole batch immediately.
4. Any add, delete, rename, or unresolved file also forces a workspace reload so Roslyn state never drifts from the real Target.
5. `ITargetAnalysisEngine.ExtractIncremental()` analyzes only the changed documents from the updated in-memory `Solution`.
6. Incremental C# indexing now compares the newly extracted `SearchText` and `BodyHash` against the persisted rows for the changed files. Matching nodes reuse their stored vector embedding, while new or stale nodes regenerate embeddings before persistence.
7. The resulting nodes and edges flow through the RelativeFilePath-targeted overwrite path coordinated by [[architecture/incremental-watch]].
8. Because the analysis engine accepts in-memory `Solution` and `Project` models directly, the default Roslyn tests can exercise extraction through `AdhocWorkspace` instead of temp directories.

## Dependencies

- Roslyn `MSBuildWorkspace` and `Microsoft.Build.Locator` for loading C# Targets.
- `IWorkspaceLoader`, `WorkspaceLoader`, `MsBuildWorkspaceFactory`, and `MsBuildLocatorRegistration` for workspace lifecycle management.
- `ITargetAnalysisEngine`, `NodeExtractor`, and `EdgeExtractor` for syntax and semantic extraction over already-loaded Roslyn models.
- `IRepositoryWorkspace` for repository-relative project and document paths.
- [[architecture/virtual-file-system]] for filesystem-backed path and content access.
