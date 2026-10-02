# SQLite persistence

Each registered workspace owns `<home>/workspaces/<workspace-guid>/index.db`. The default home is `~/.sharpsense`; `SHARPSENSE_HOME` can select another absolute path. Workspaces never share graph rows or memories merely because they use the same repository.

## Persisted model

| Table | Purpose |
| --- | --- |
| `GraphNodes` | Integer graph identity, unique canonical ID, and node kind. |
| `Directories` / `DirectoryClosures` | Repository-relative directory hierarchy and descendant relationships. |
| `Documents` | Indexed file identity and directory membership. |
| `ProjectNodes` | Project graph metadata and associated project document. |
| `CodeNodes` | Symbol/document-chunk metadata, source locations, search text, hashes, and embeddings. |
| `DependencyEdges` | Typed relationships between graph nodes. |
| `MemoryNodes` | Immutable Markdown notes attached to code-node IDs. |
| `IndexRunState` | Bounded records of the latest successful index and latest attempt. |

FTS5 provides keyword lookup; sqlite-vec supplies vector-distance operations. The workspace explorer projects from normalized directory/document/project tables rather than a dedicated tree table. Target-framework variants share one project document, which appears once in the file tree. The migration permits that sharing without replacing existing graph rows or memories.

## Identity and memories

`GraphNodes.CanonicalId` is unique. Numeric node IDs are compact handles within one workspace. C# canonical identity includes project ownership, so `CodeNodes.FullyQualifiedName` is not globally unique and cannot be the memory foreign key.

`MemoryNodes.TargetCodeNodeId` references `CodeNodes.Id` with cascade deletion. The record stores the target hash, content/hash, tags, intent, embedding, and creation time. Staleness is computed by comparing its saved target hash with the current node.

Graph reconciliation retains surviving canonical identities and their memories. Removed nodes cascade-delete attached memories; this also applies when a source is removed from the workspace and the next index drops its nodes. A merged workspace starts with its own empty database and does not copy memories.

## Writes and compatibility

Selected source graphs are extracted and merged before one transactional replacement. Embeddings can be reused when their relevant content/hash is unchanged. A required extraction failure leaves the previous committed graph intact; diagnostics record the failed attempt separately.

An OS-held indexing lease excludes concurrent index/watch writers and source mutations for the same workspace. CLI analysis takes the lease before database initialization. Read operations remain workspace-scoped. Impact analysis keeps traversal and endpoint lookup in one SQLite read transaction, so concurrent indexing cannot mix graph snapshots.

Catalog listing/resolution and doctor do not create or migrate a database. Supported schema upgrades use migrations; incompatible legacy data is preserved and reported rather than automatically deleted or silently adopted into a named workspace.

See [workspace storage](../cli/workspace-command.md#storage), [hybrid search](../architecture/hybrid-search-pipeline.md), and [graph statistics](../cli/doctor-command.md).
