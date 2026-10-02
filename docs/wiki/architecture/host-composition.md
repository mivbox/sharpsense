# Host composition

CLI, MCP, and concurrent UI requests must retain a fixed workspace identity while resolving database and indexing services.

`AbstractAsyncCommand` manages command host lifecycle and errors. `WorkspaceCommandServiceCollectionExtensions` registers deferred selection from `IWorkspaceCatalog`; `WorkspaceSelection` carries the definition, home-owned configuration path, and `IRepositoryWorkspace`. Its typed sources populate indexing options. `WorkspaceDefinition.WorkspaceRoot` is the configured path, not necessarily a Git root. A YAML-only read model accepts the legacy `repositoryRoot` key; writes use `workspaceRoot` while transport payloads retain their existing field names.

Memory, context, and trace commands register the embedding infrastructure required by their memory repository. The generator remains lazy, so reading or deleting a memory does not run inference.

Query commands and MCP bind one workspace per host. CLI selection uses an explicit workspace or the saved global default. MCP uses an explicit workspace or `ResolveFromDirectory` to find exactly one root containing its launch directory, without consulting the CLI default. Selection is fixed at startup; overlapping roots are ambiguous. `WorkspaceSetup` handles creation and interactive analysis selection through CLI-only `IWorkspaceInteractions` before creating a bound execution scope. Bare `workspace` shows subcommand help. `WorkspaceExecutionServiceCollectionExtensions` shares explicit scope registration with the web host. Analyze/watch retain one dependency-injection scope per session, keeping Roslyn state and change filtering together. The indexing lease starts before database initialization and lasts through teardown.

The UI has a global catalog. Workspace-specific HTTP operations bind `IWorkspaceScope` from the required `X-SharpSense-Workspace` header; indexing routes select a workspace by route ID and create a separate job scope. Query services and the database factory resolve the selected workspace from that scope. The optional UI startup workspace is an initial view hint, not global mutable state. Its validated loopback URL overrides ambient Kestrel endpoint configuration.

`IAnalysisNotifier` and immutable notification/snapshot contracts belong to the application indexing slice. The internal `AnalysisSnapshotStore` in CLI shared presentation reduces notifications for both Spectre and browser SSE. Language workers publish typed progress; terminal rendering and browser SSE consume bounded state without blocking extraction. Spectre prompts stay in `IWorkspaceInteractions`, and terminal rendering stays in its presenter. Shared source discovery returns application source models; HTTP endpoints map them into API models.

Minimal HTTP endpoints each live in a separate operation-named endpoint file. Each endpoint exposes a route-builder extension and a named handler; feature route-group extension files compose those endpoints. Transport models stay in each feature's `Models/` directory. Routes, operation names and response contracts remain stable for the generated UI client.

`UiApiServiceCollectionExtensions` owns HTTP service registration, `UiApiExtensions` composes middleware and routes, and `UiProblemResults` maps expected failures to problem responses. Shared compact CLI JSON settings belong to `CliJsonOptions`; TOON formatting remains in `TokenObjectNotation`.

Persistence registration stays in `PersistenceServiceCollectionExtensions`. `WorkspaceDatabaseStartup` owns the startup scope, and `WorkspaceDatabaseMigrator` performs the shared compatibility check and migration for startup and on-demand initialization. `WorkspaceDatabaseInitializer` retains its per-path gate and completed-state cache. Incompatible databases remain preserved.

## Responsibilities

| Component | Responsibility |
| --- | --- |
| `IWorkspaceCatalog` | Definition validation, registration, selection, merge, atomic source edits, and indexing leases. |
| `WorkspaceSelection` | Stable workspace ID, source snapshot, root, and storage paths. |
| `IWorkspaceScope` | Bind an analysis session, UI request, or job once before resolving dependent services. |
| `IRepositoryWorkspace` | Workspace-specific root, database path, and path normalization. |
| `IWorkspaceDatabaseInitializer` | Initialize the selected workspace database when needed. |
| `SharpSenseHome` | Resolve the absolute override or default `~/.sharpsense` home. |

Catalog reads, doctor and MCP startup do not create or migrate databases. MCP initializes storage when a graph or memory tool needs it; `graph_stats` and command execution remain available independently. Logs use the same home under `logs/`. Production configuration does not read project-local YAML or synthesize an implicit workspace.

See [analyze](../cli/analyze-command.md), [MCP](../cli/mcp-command.md), and [UI](../cli/ui-command.md).

Infrastructure registers internal workspace storage implementations behind these contracts. UI overview counts and trace dependencies use application read contracts rather than accessing the EF context from endpoints. `WorkspaceExecutionOptions` carries the bound workspace sources and embedding settings; Markdown patterns come directly from those sources, with no separate configuration monitor.
