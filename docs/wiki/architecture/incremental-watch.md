---
title: "Incremental Watch"
type: architecture
tags: [incremental-watch, cqrs, implemented]
created: 2026-04-26
updated: 2026-05-02
confidence: high
---

## The Problem

Watch mode has to batch noisy filesystem events, preserve correct rename and delete semantics, and recover from watcher failures without corrupting the index or forcing the CLI to guess what changed.

## The Approach

`AnalyzeCommand` always performs a full Target index first. When `--watch` is enabled, it resolves the repository root from `IOptions<SharpSenseCliOptions>` and hands control to `IWorkspaceWatcher`. `WorkspaceWatcher` now lives on the [[architecture/virtual-file-system]] boundary: it receives `IFileSystem` and `IFileSystemWatcherFactory`, constructs the watcher through the factory, tracks known directories through the injected filesystem, and uses a semaphore plus a 250 ms debounce window to emit batches. Relevant events are reduced to `WorkspaceFileChange` records for C# and Markdown files only. The callback dispatches `UpdateWorkspaceFilesCommand`; for modified C# files, `WorkspaceLoader` now retries transient read failures and then falls back to reloading the Roslyn workspace before the CLI-level recovery path is considered. The new CLI/MCP refactor surfaces write the edited C# document through Roslyn only and then rely on this same watcher path to refresh the persisted graph/vector store asynchronously; they do not dispatch `UpdateWorkspaceFilesCommand` directly. If the batch handler still fails after that loader-level recovery, `AnalyzeCommand` runs a full reindex and continues inside the current watcher session. If the watcher itself reports a fatal error, `AnalyzeCommand` runs a full reindex and then restarts the outer watch loop. The incremental persistence side of this flow is documented in [[persistence/sqlite-schema]] and the write entrypoints are documented in [[cli/refactor-command]].

## Components Involved

| Component | Role |
| --- | --- |
| `AnalyzeCommand` | Owns watch-mode startup, recovery, and restart decisions. |
| `IWorkspaceWatcher` | Application contract for debounced workspace change batches. |
| `IFileSystemWatcherFactory` | Creates the concrete watcher at the infrastructure edge without hiding the dependency. |
| `IFileSystem` | Supplies directory existence checks and directory-rename expansion without direct `System.IO` calls. |
| `WorkspaceWatcher` | `FileSystemWatcher`-based implementation with queueing, debounce, and fatal-error signaling. |
| `WorkspaceFileChange` | Captures added, modified, deleted, and renamed file events. |
| `UpdateWorkspaceFilesCommand` | CQRS payload that carries the changed-file batch into the indexing pipeline. |
| `RefactorCommand` / MCP `refactor_node` | Write-capable entrypoints that stop at the Roslyn disk mutation and rely on the watcher for downstream index refresh. |
| `KnowledgeGraphIndexing` | Persists the resulting incremental node and edge updates by repository-relative file path. |

## Strict Rules

1. Emit change batches only for C# and Markdown paths, because those are the only indexed Target asset types.
2. Construct watchers through `IFileSystemWatcherFactory`, not `new FileSystemWatcher()`.
3. Keep recovery ownership in the CLI: `IWorkspaceWatcher` surfaces fatal infrastructure failures, while `AnalyzeCommand` decides when to reindex or restart.
4. Persist incremental changes by repository-relative `RelativeFilePath`, not by whole-database resets, whenever a batch can be handled incrementally.
5. Align rename and discovery semantics with [[architecture/file-discovery]]. Directory-rename expansion may enumerate through `IFileSystem`, but it must preserve the same extension and ignore behavior as full discovery.
6. Treat transient modified-file instability as a Roslyn workspace-refresh concern first; only escalate to CLI full-index recovery when loader retries and workspace reload cannot keep the batch incremental.
7. Write-capable surfaces such as [[cli/refactor-command]] and MCP `refactor_node` must stop at the Roslyn file mutation boundary; watcher-driven indexing remains the single path that updates persistence asynchronously after those writes.
