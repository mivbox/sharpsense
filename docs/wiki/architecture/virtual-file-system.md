---
title: "Virtual File System"
type: architecture
tags: [filesystem, dependency-injection, implemented]
created: 2026-04-28
updated: 2026-04-28
confidence: high
---

## The Problem

Indexing, configuration, persistence, and watch mode all touch disk-backed resources. When those collaborators call `File`, `Directory`, or `new FileSystemWatcher()` directly, the dependency graph becomes hidden, tests need temp directories or user-profile paths, and Linux CI picks up brittle physical-I/O behavior that has nothing to do with the indexing rules themselves.

## The Approach

SharpSense owns the filesystem boundary in Infrastructure. `AddFileSystem()` registers `IFileSystem` plus `IFileSystemWatcherFactory`, and long-lived infrastructure collaborators consume those abstractions through constructor injection. `RepositoryWorkspaceFactory` now owns repository-root discovery plus database-path derivation so callers depend on `IRepositoryWorkspace` instead of a static workspace constructor. `RepositoryWorkspace`, `WorkspaceFileDiscoverer`, `DocumentDiscoverer`, `WorkspaceWatcher`, `WorkspaceLoader`, `SharpSenseConfigurationExtensions`, and `PersistenceServiceCollectionExtensions` all read or create paths through `IFileSystem` instead of direct `System.IO` calls. The Roslyn side is split so `IWorkspaceLoader` owns disk-backed `MSBuildWorkspace` loading, while `ITargetAnalysisEngine` only analyzes already-loaded `Solution` or `Project` models. The Markdown side mirrors that separation by making `DocumentDiscoverer` depend on `IMarkdownIndexer` instead of a concrete indexer. This keeps physical-edge behavior explicit while letting tests swap in `MockFileSystem`, manual options change tokens, and `AdhocWorkspace`. See [[architecture/file-discovery]], [[architecture/incremental-watch]], [[extractors/csharp]], and [[extractors/markdown]].

## Components Involved

| Component | Role |
| --- | --- |
| `FileSystemServiceCollectionExtensions` | Registers the default `IFileSystem` and `IFileSystemWatcherFactory` inside Infrastructure. |
| `IFileSystem` | Canonical abstraction for file reads, writes, directory queries, and path operations in long-lived infrastructure code. |
| `IFileSystemWatcherFactory` | Explicit seam for `FileSystemWatcher` creation so watch mode stays injectable. |
| `IRepositoryWorkspaceFactory` | Builds repository workspaces from working directories without leaking static creation helpers into callers. |
| `IWorkspaceLoader` | Owns MSBuild-backed workspace loading, caching, and document refresh for C# Targets. |
| `ITargetAnalysisEngine` | Analyzes already-loaded Roslyn models without opening Targets from disk itself. |
| `IMarkdownIndexer` | Converts raw Markdown plus canonical relative paths into document nodes and edges. |
| `SharpSenseConfigurationExtensions` | Reads `sharpsense.yaml` through `IFileSystem` and leaves change-token watching overrideable for tests. |
| `PersistenceServiceCollectionExtensions` | Keeps SQLite directory creation at the persistence boundary rather than inside `RepositoryWorkspace`. |

## Strict Rules

1. Register the default `IFileSystem` boundary in Infrastructure extension methods, not in `Program.cs` and not in CLI route composition.
2. Long-lived infrastructure collaborators must take filesystem, watcher, workspace-factory, loader, or indexer dependencies through constructor injection; do not hide them behind `new`.
3. Treat direct `File`, `Directory`, `FileInfo`, and `DirectoryInfo` calls in long-lived services as architectural violations. Route them through `IFileSystem`.
4. Physical-edge exceptions such as `PhysicalFileProvider`, `FileSystemWatcher`, and SQLite file paths are allowed only at explicit boundaries and must remain replaceable in the default test suite.
5. Stateless tests use `MockFileSystem`, manual `IOptionsChangeTokenSource<SharpSenseConfig>` implementations, Moq, and Roslyn `AdhocWorkspace` instead of temp directories or copied fixture trees.
