---
title: "CSharp"
type: extractor
tags: [csharp, roslyn, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## Target Language

C#. `CSharpLanguageExtractor` is the Roslyn-backed extractor that turns a Target into project nodes, code nodes, dependency edges, and diagnostics.

## Full Index Logic

1. `CSharpLanguageExtractor.Extract()` sends the Target path, repository workspace, a fresh `RoslynWorkspaceOptions`, and the optional progress reporter to `IRoslynTargetAnalysisEngine`.
2. `RoslynTargetAnalysisEngine` asks `WorkspaceLoader` for an MSBuild-backed workspace snapshot of the Target path.
3. `WorkspaceLoader` uses `MsBuildWorkspaceFactory`, which ensures `MSBuildLocator` is registered and loads the C# workspaces assemblies before opening either a project file or a `.sln`.
4. `BuildProjectNodes()` creates repository-relative project ids and project nodes for the ordered projects in the workspace snapshot.
5. `NodeExtractor` walks the ordered projects and documents to emit `CodeNode` records plus the symbol maps needed for edge construction.
6. `EdgeExtractor` consumes the workspace snapshot and symbol maps to emit dependency edges for the same Target.
7. The extractor returns projects, code nodes, edges, and diagnostics to the indexing pipeline described in [[persistence/sqlite-schema]].

## Incremental Logic

1. `ExtractIncremental()` filters the incoming batch to C#-affected changes only.
2. `RoslynTargetAnalysisEngine` normalizes the changed paths against the repository root and ensures the workspace snapshot is loaded.
3. `WorkspaceLoader.UpdateDocuments()` updates modified documents in place when possible; any add, delete, rename, or unresolved file forces a workspace reload.
4. Only the changed documents are re-extracted, and only their incremental edges are emitted.
5. The resulting nodes and edges flow through the RelativeFilePath-targeted overwrite path coordinated by [[architecture/incremental-watch]].

## Dependencies

- Roslyn `MSBuildWorkspace` and `Microsoft.Build.Locator` for loading C# Targets.
- `WorkspaceLoader`, `MsBuildWorkspaceFactory`, and `MsBuildLocatorRegistration` for workspace lifecycle management.
- `NodeExtractor` and `EdgeExtractor` for syntax and semantic extraction.
- `IRepositoryWorkspace` for repository-relative project and document paths.
