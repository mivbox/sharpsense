# Host composition

CLI, MCP, and concurrent UI requests must retain a fixed workspace identity while resolving database and indexing services.

`AbstractAsyncCommand` manages command host lifecycle and errors. `WorkspaceCommandServices` registers deferred selection from `WorkspaceCatalog`; `WorkspaceSelection` carries the definition, home-owned configuration path, and `IRepositoryWorkspace`. Its typed sources populate indexing options.

Query commands and MCP bind one workspace per host. CLI selection uses an explicit workspace, then the saved global default, then repository lookup; MCP requires an explicit workspace. `WorkspaceSetup` handles creation and interactive analysis selection through CLI-only `IWorkspaceInteractions` before creating a bound execution scope. Bare `workspace` shows subcommand help. `WorkspaceExecutionServices` shares explicit scope registration with the web host. Analyze/watch retain one dependency-injection scope per session, keeping Roslyn state and change filtering together. The indexing lease starts before database initialization and lasts through teardown.

The UI has a global catalog. Workspace-specific HTTP operations bind `WorkspaceScope` from the required `X-SharpSense-Workspace` header; indexing routes select a workspace by route ID and create a separate job scope. Query services and the database factory resolve the selected workspace from that scope. The optional UI startup workspace is an initial view hint, not global mutable state.

`IAnalysisNotifier` and immutable `AnalysisSnapshotStore` snapshots belong to the application indexing slice. Language workers publish typed progress; terminal rendering and browser SSE consume bounded state without blocking extraction. Spectre prompts stay in `IWorkspaceInteractions`, and terminal rendering stays in its presenter. Shared source discovery returns application source models; HTTP endpoints map them into API DTOs.

## Responsibilities

| Component | Responsibility |
| --- | --- |
| `WorkspaceCatalog` | Definition validation, registration, selection, merge, atomic source edits, and indexing leases. |
| `WorkspaceSelection` | Stable workspace ID, source snapshot, root, and storage paths. |
| `WorkspaceScope` | Bind an analysis session, UI request, or job once before resolving dependent services. |
| `IRepositoryWorkspace` | Workspace-specific root, database path, and path normalization. |
| `WorkspaceDatabaseInitializer` | Initialize the selected workspace database when needed. |
| `SharpSenseHome` | Resolve the absolute override or default `~/.sharpsense` home. |

Catalog reads and doctor do not create or migrate databases. Logs use the same home under `logs/`. Production configuration does not read project-local YAML or synthesize an implicit workspace.

See [analyze](../cli/analyze-command.md), [MCP](../cli/mcp-command.md), and [UI](../cli/ui-command.md).
