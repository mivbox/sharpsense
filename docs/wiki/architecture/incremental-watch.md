---
title: "Incremental Watch"
type: architecture
tags: [incremental-watch, cqrs, implemented]
created: 2026-04-26
updated: 2026-05-04
confidence: high
---

## The Problem

Watch mode has to batch noisy filesystem events, preserve correct rename and delete semantics, and recover from watcher failures without corrupting the index or forcing the CLI to guess what changed.

## The Approach

`AnalyzeCommand` always performs a full Target index first. When `--watch` is enabled, it resolves the repository root from `IOptions<SharpSenseCliOptions>` and hands control to `IWorkspaceWatcher`. `WorkspaceWatcher` now lives on the [[architecture/virtual-file-system]] boundary with a dual-watcher topology: a filtered file watcher handles high-frequency file I/O, while a directory watcher listens only for directory-name changes. Both watchers are constructed through `IFileSystemWatcherFactory`, share the same debounce queue, and keep the `knownDirectories` cache in sync through the injected `IFileSystem`. Fast-path `.git` events are dropped before any directory bookkeeping. File events still reduce to file-scoped `WorkspaceFileChange` records, but relevant directory deletes and renames now emit `DirectoryDeleted` and `DirectoryRenamed` actions instead of forcing immediate watcher-side recovery or enumerating the disk inside the watcher. `UpdateWorkspaceFilesCommandHandler` expands those directory actions by combining persisted old document paths from `IKnowledgeGraphRepository` with new-side discovery from `IWorkspaceFileDiscoverer`, producing the file-level batch consumed by the existing C# and Markdown extractors plus `ReplaceWorkspaceFiles(...)`. For modified C# files, `WorkspaceLoader` still retries transient reads and then falls back to reloading the Roslyn workspace before the CLI-level recovery path is considered. The CLI/MCP refactor entrypoints still stop at the Roslyn write boundary and rely on this same watcher pipeline to refresh persistence asynchronously. If the batch handler still fails after loader-level recovery and directory expansion, `AnalyzeCommand` runs a full reindex and continues inside the current watcher session. If the watcher itself reports a fatal error, `AnalyzeCommand` runs a full reindex and then restarts the outer watch loop. The incremental persistence side of this flow is documented in [[persistence/sqlite-schema]] and the write entrypoints are documented in [[cli/refactor-command]].

## Components Involved

| Component | Role |
| --- | --- |
| `AnalyzeCommand` | Owns watch-mode startup, recovery, and restart decisions. |
| `IWorkspaceWatcher` | Application contract for debounced workspace change batches. |
| `IFileSystemWatcherFactory` | Creates the concrete watcher at the infrastructure edge without hiding the dependency. |
| `IFileSystem` | Supplies directory existence checks and directory-rename expansion without direct `System.IO` calls. |
| `WorkspaceWatcher` | `FileSystemWatcher`-based implementation with paired file/directory watchers, queueing, debounce, and fatal-error signaling. |
| `WorkspaceFileChange` | Captures file actions plus directory delete/rename actions before handler-side expansion. |
| `UpdateWorkspaceFilesCommand` | CQRS payload that carries the changed-file batch into the indexing pipeline. |
| `UpdateWorkspaceFilesCommandHandler` | Expands directory actions into file-level replacements before extraction and persistence. |
| `IWorkspaceFileDiscoverer` | Discovers new-side files for directory renames through the canonical discovery boundary. |
| `IKnowledgeGraphRepository` | Supplies persisted old document paths and replaces the merged incremental file set. |
| `RefactorCommand` / MCP `refactor_node` | Write-capable entrypoints that stop at the Roslyn disk mutation and rely on the watcher for downstream index refresh. |
| `KnowledgeGraphIndexing` | Persists the resulting incremental node and edge updates by repository-relative file path. |

## Strict Rules

1. Emit file-scoped change batches only for C# and Markdown paths, but allow `DirectoryDeleted` and `DirectoryRenamed` for relevant directories because the handler expands them into those same file-level targets.
2. Construct watchers through `IFileSystemWatcherFactory`, not `new FileSystemWatcher()`, and keep the split between the filtered file watcher and the directory-name watcher.
3. Apply the `.git` fast-path ignore before directory bookkeeping so source-control churn never mutates `knownDirectories` or spends queue capacity.
4. Do not enumerate directory trees or force full reindex recovery inside `WorkspaceWatcher` for relevant directory deletes and renames; that translation belongs in `UpdateWorkspaceFilesCommandHandler`.
5. Align rename discovery semantics with [[architecture/file-discovery]] by using `IWorkspaceFileDiscoverer` for the new-side directory view and persisted document paths for the old-side delete view.
6. Keep recovery ownership in the CLI: `IWorkspaceWatcher` surfaces fatal infrastructure failures, while `AnalyzeCommand` decides when to reindex or restart.
7. Persist incremental changes by repository-relative `RelativeFilePath`, not by whole-database resets, whenever a batch can be handled incrementally.
8. Treat transient modified-file instability as a Roslyn workspace-refresh concern first; only escalate to CLI full-index recovery when loader retries and workspace reload cannot keep the batch incremental.
9. Write-capable surfaces such as [[cli/refactor-command]] and MCP `refactor_node` must stop at the Roslyn file mutation boundary; watcher-driven indexing remains the single path that updates persistence asynchronously after those writes.
