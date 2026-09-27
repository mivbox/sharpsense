# Commands and queries

Application features expose command/query handlers through shared interfaces. CLI, MCP, and HTTP adapters select the workspace, bind input, invoke the handler, and format the result.

`IndexWorkspaceCommandHandler` owns indexing orchestration: build the explicit source plan, coordinate bounded asynchronous language workers, merge their contributions, reuse or generate embeddings, and commit through `IKnowledgeGraphRepository`. Sources within each language run serially; independent languages can run concurrently. It records indexing phases and diagnostics. `UpdateWorkspaceFilesCommandHandler` filters watch events and routes relevant named-workspace changes through the same complete-graph reconciliation, reusing safe committed contributions for documentation-only edits. Cache publication follows the database commit, and failure or cancellation invalidates reusable state.

Read slices expose focused contracts for hybrid search, node context, traces, inheritors, workspace exploration, graph loading, and statistics. Infrastructure implements their query boundaries against the selected database. Memory commands share a repository boundary for add/delete and retrieval.

`ExecuteProcessCommandHandler` owns process execution and bounded output reduction behind process/log-index contracts. CLI `execute` and MCP `ctx_execute` invoke the same handler; Infrastructure owns process startup and the transient SQLite output index.

`GetNodeContextQueryHandler` returns typed results for invalid or missing node IDs. CLI writes expected failures to stderr and returns a nonzero exit code; MCP marks its tool result with `isError: true`. HTTP maps failures to problem responses without a separate existence lookup. Cancellation and unexpected storage failures still propagate.

## Boundaries

- Domain owns graph concepts and identities.
- Application owns use cases, source-plan contracts, and orchestration.
- Infrastructure owns Roslyn, Tree-sitter, Markdig, filesystem integration, embeddings, and SQLite persistence.
- CLI owns command/MCP presentation and the HTTP host.
- UI features use generated API contracts and workspace-scoped query clients.

Keep command/query models beside their handlers, shared feature models in the feature's `Models/`, and infrastructure-facing interfaces in `Abstractions/`. See [vertical slices](vertical-slice-application.md).

Expected indexing failures return results and retain the previous graph. Transport adapters convert failures to appropriate command exit codes or HTTP responses. A committed graph update is distinct from an ignored filesystem event, which must not imply new index data.

Workspace identity stays explicit for each host, request, or background job; see [host composition](host-composition.md).

Indexing has one persistence contract: reconcile the complete selected graph. There is no file-delta extractor or repository API. `GraphSnapshot` normalizes and compares graphs, `PersistedGraphBuilder` preserves identities while constructing records, and `GraphPersistence` performs database operations. `KnowledgeGraphRepository` owns the transaction and revision change; the extraction cache is published only after commit.
