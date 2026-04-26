---
title: "Incremental Watch"
type: architecture
tags: [incremental-watch, cqrs, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## The Problem

Watch mode has to batch noisy filesystem events, preserve correct rename and delete semantics, and recover from watcher failures without corrupting the index or forcing the CLI to guess what changed.

## The Approach

`AnalyzeCommand` always performs a full Target index first. When `--watch` is enabled, it resolves the repository root from `IOptions<SharpSenseCliOptions>` and hands control to `IWorkspaceWatcher`. `WorkspaceWatcher` wraps `FileSystemWatcher`, buffers relevant changes in a concurrent queue, and uses a semaphore plus a 250 ms debounce window to emit batches. Relevant events are reduced to `WorkspaceFileChange` records for C# and Markdown files only. The callback dispatches `UpdateWorkspaceFilesCommand`; if the batch handler fails, `AnalyzeCommand` runs a full reindex and continues inside the current watcher session. If the watcher itself reports a fatal error, `AnalyzeCommand` runs a full reindex and then restarts the outer watch loop. The incremental persistence side of this flow is documented in [[persistence/sqlite-schema]].

## Components Involved

| Component | Role |
| --- | --- |
| `AnalyzeCommand` | Owns watch-mode startup, recovery, and restart decisions. |
| `IWorkspaceWatcher` | Application contract for debounced workspace change batches. |
| `WorkspaceWatcher` | FileSystemWatcher-based implementation with queueing, debounce, and fatal-error signaling. |
| `WorkspaceFileChange` | Captures added, modified, deleted, and renamed file events. |
| `UpdateWorkspaceFilesCommand` | CQRS payload that carries the changed-file batch into the indexing pipeline. |
| `KnowledgeGraphIndexing` | Persists the resulting incremental node and edge updates by repository-relative file path. |

## Strict Rules

1. Emit change batches only for C# and Markdown paths, because those are the only indexed Target asset types.
2. Keep recovery ownership in the CLI: `IWorkspaceWatcher` surfaces fatal infrastructure failures, while `AnalyzeCommand` decides when to reindex or restart.
3. Persist incremental changes by repository-relative `RelativeFilePath`, not by whole-database resets, whenever a batch can be handled incrementally.
4. Align rename and discovery semantics with [[architecture/file-discovery]] so watch mode respects the same allow-list and path-normalization rules as full indexing.
5. The current directory-rename expansion still uses `Directory.EnumerateFiles()` plus a hard-coded ignore list rather than `IWorkspaceFileDiscoverer`. Treat that as a documented compliance gap to close.
