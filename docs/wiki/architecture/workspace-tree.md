---
title: "Workspace Tree"
type: architecture
tags: [sqlite, cqrs, incremental-watch, implemented]
created: 2026-04-29
updated: 2026-04-30
confidence: high
---

## The Problem

The UI previously loaded the full dependency graph up front and had no persisted hierarchy for lazy scope selection. Reconstructing that hierarchy from the live repository filesystem at runtime would bypass analyzed data, drift from persisted graph state, and make large workspaces expensive to browse. Even after the explorer became lazy, shipping nodes and edges through one payload still made enterprise monorepo renders pay the edge cost too early.

## The Approach

SharpSense now materializes a `WorkspaceTreeNode` read model during indexing from analyzed `ProjectNode.RelativeFilePath` and `CodeNode.RelativeFilePath` values. `KnowledgeGraphRepository` rebuilds that table during full target writes and refreshes it after incremental file updates, so the explorer reads a stable database-backed hierarchy instead of walking disk. `UiCommand` exposes `/api/tree?path=...` for lazy child expansion, `/api/graph/nodes?directoryIds=...` for initial scoped node hydration, and `/api/graph/edges?directoryIds=...` for explicit edge hydration after the user enables edges. The React Solution Explorer still keeps the graph empty until the user selects scope, but large scopes now render nodes first and defer edge cost until the canvas is already anchored.

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
| `GetDependencyGraphNodesQueryHandler` / `GetDependencyGraphEdgesQueryHandler` | CQRS entrypoints for the split graph hydration flow. |
| `UiCommand` | Hosts `/api/tree`, `/api/graph/nodes`, and `/api/graph/edges` alongside the embedded UI assets. |

## Strict Rules

1. Never walk the repository filesystem to populate the explorer at runtime; the UI must read from `WorkspaceTreeNodes`.
2. Materialize folder, project, and file rows from analyzed `RelativeFilePath` data inside the `IKnowledgeGraphRepository` write path rather than in the web host.
3. Treat workspace-tree `Path` values as the canonical frontend selection keys and translate them to persisted `directoryIds` before calling `/api/graph/nodes` or `/api/graph/edges`.
4. Keep the explorer read side in the `WorkspaceExplorer` vertical slice and register it through `AddWorkspaceExplorer()` and `AddWorkspaceExplorerInfrastructure()`.
5. Preserve the opt-in contract: node hydration starts only after explicit workspace-tree selection, and edge hydration starts only after explicit edge enablement.
