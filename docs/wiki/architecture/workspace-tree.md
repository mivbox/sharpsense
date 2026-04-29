---
title: "Workspace Tree"
type: architecture
tags: [sqlite, cqrs, incremental-watch, implemented]
created: 2026-04-29
updated: 2026-04-29
confidence: high
---

## The Problem

The UI previously loaded the full dependency graph up front and had no persisted hierarchy for lazy scope selection. Reconstructing that hierarchy from the live repository filesystem at runtime would bypass analyzed data, drift from persisted graph state, and make large workspaces expensive to browse.

## The Approach

SharpSense now materializes a `WorkspaceTreeNode` read model during indexing from analyzed `ProjectNode.RelativeFilePath` and `CodeNode.RelativeFilePath` values. `KnowledgeGraphRepository` rebuilds that table during full target writes and refreshes it after incremental file updates, so the explorer reads a stable database-backed hierarchy instead of walking disk. `UiCommand` exposes `/api/tree?path=...` for lazy child expansion and `/api/graph?paths=...` for opt-in scoped graph reads, while the React Solution Explorer virtualizes the visible rows and keeps the graph empty until the user selects scope.

This pattern builds on [[cli/ui-command]], persists through [[persistence/sqlite-schema]], and keeps the read side inside the Application slice rules from [[architecture/vertical-slice-application]].

## Components Involved

| Component | Role |
| --- | --- |
| `WorkspaceTreeNode` | Persisted folder/project/file row keyed by repository-relative path. |
| `WorkspaceTreeNodeConfiguration` | Maps the table and indexes for `ParentId`, `Path`, and `ProjectId`. |
| `SharpSenseDbContext.WorkspaceTreeNodes` | EF boundary for the persisted tree read model. |
| `KnowledgeGraphRepository` | Rebuilds the workspace tree during full and incremental graph persistence. |
| `IWorkspaceTreeRepository` | Application-facing read boundary for lazy explorer queries. |
| `WorkspaceTreeRepository` | Returns immediate children for a persisted tree path. |
| `GetWorkspaceTreeQueryHandler` | CQRS query entrypoint for `/api/tree`. |
| `UiCommand` | Hosts `/api/tree` and `/api/graph` alongside the embedded UI assets. |

## Strict Rules

1. Never walk the repository filesystem to populate the explorer at runtime; the UI must read from `WorkspaceTreeNodes`.
2. Materialize folder, project, and file rows from analyzed `RelativeFilePath` data inside the `IKnowledgeGraphRepository` write path rather than in the web host.
3. Treat workspace-tree `Path` values as the canonical selection keys for both `/api/tree` and `/api/graph?paths=...`.
4. Keep the explorer read side in the `WorkspaceExplorer` vertical slice and register it through `AddWorkspaceExplorer()` and `AddWorkspaceExplorerInfrastructure()`.
5. Preserve the opt-in contract: the graph API returns an empty graph until explicit workspace-tree paths are selected.
