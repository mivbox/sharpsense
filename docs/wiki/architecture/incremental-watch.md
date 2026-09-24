# Watch reconciliation

Watch mode maintains the graph for the selected workspace. Every successful update commits one complete graph, while the active watch session can reuse unchanged language contributions after safe documentation-only edits.

## Processing a change

1. Repository watchers batch file and directory events.
2. `WorkspaceChangeFilter` checks the workspace's sources and relevant dependency/config inputs.
3. Ignored batches return without claiming an index commit.
4. Relevant batches invoke the named source plan, carrying changed-file information for adapters that can reuse state. Documentation-only file changes may reuse previously committed C# and TypeScript contributions while extracting the selected Markdown set again.
5. Independent language groups run asynchronously, with at most three groups active. Sources within one language run serially so adapters do not concurrently mutate shared language state.
6. Contributions are merged in source-plan order, embeddings are reused or generated, and one complete graph snapshot is committed. Reusable contributions become current only after that commit succeeds.

A failed extraction leaves the last committed graph available. Failure or cancellation invalidates the reusable contribution cache; unfinished workers must settle before the session is disposed. UI watch records the failure and waits for another change; CLI watch includes recovery handling for watcher/update failures. Neither behavior makes a failed index current.

## Language behavior

| Language | Relevant inputs and reconciliation |
| --- | --- |
| C# | Selected project/solution inputs, source membership, linked source files, and compilation/generator inputs. Existing Markdown file modifications or deletions reuse the committed C# contribution only when those paths are absent from Roslyn source, additional, and analyzer-config documents. Added or renamed documentation refreshes C# conservatively because new files can match `AdditionalFiles` globs. Suitable C# source edits can update a warm Roslyn workspace; structural and configuration changes require reloading. |
| TypeScript | Selected files, imports, aliases, extended configurations, and project references. Documentation-only file changes can reuse the committed contribution. Code/configuration changes rebuild the selected dependency graph because consumers outside the edited file may be affected. |
| Markdown | Selected documentation paths and directory changes. Rebuilding the selected document set restores links when targets appear, disappear, or change headings. Supported documentation-only extensions are `.md`, `.markdown`, `.mdown`, and `.mkd`. |

Contribution reuse requires the same scoped watch session, a successful prior commit, and unchanged selected sources. Full analysis, code/configuration changes, mixed-language changes, and directory events take the conservative extraction path. Standard SDK analyzer references alone do not disable safe existing-document reuse.

Complete reconciliation prevents stale edges or abandoned documents from surviving merely because they were outside the latest event batch. Persisted canonical identities and unchanged embeddings can still be reused. Verbose logs identify each source contribution as `extracted` or `reused`, allowing cache behavior to be checked without inferring it from elapsed time or unchanged IDs.

Native filesystem notifications can report both creation and modification when an existing Markdown file is saved. After initialization, the watcher uses known Markdown paths to classify these duplicate creation notifications as modifications. Previously unseen paths remain additions, even when the first native notification says modification. Initial reconciliation and renames retain conservative handling so newly discovered generator inputs cannot be mistaken for safe existing-document edits.

## Lifecycle

A filesystem lease permits one index/watch writer per workspace across CLI and UI processes. Source mutations take the same lease. Stop watch before changing the workspace definition, then restart; configuration is a session snapshot, not a live-reloaded input.

Filesystem subscriptions start before the initial index. Edits queued during that index are reconciled before watch reports itself ready; cancellation stops the session and releases its subscriptions and lease.

The lease is an open OS handle on `.index.lock`; a leftover file does not indicate a running watcher. UI job status describes jobs owned by that UI host, while [graph statistics](../cli/doctor-command.md) describe persisted indexing outcomes.

See [analyze](../cli/analyze-command.md), [source discovery](file-discovery.md), and [SQLite persistence](../persistence/sqlite-schema.md).
