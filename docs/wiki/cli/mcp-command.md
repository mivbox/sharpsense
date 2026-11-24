# MCP server

Start a stdio MCP server bound to a registered workspace:

```bash
sharpsense mcp                      # Discover one workspace containing this directory
sharpsense mcp --workspace product  # Bind explicitly
```

Configure your MCP client to launch executable `sharpsense` with arguments `["mcp", "--workspace", "product"]`. Pass the same absolute `SHARPSENSE_HOME` environment value if the workspace uses a custom home. The workspace must be registered and analyzed before graph queries are useful.

Without `--workspace`, MCP selects the single registered root containing its launch directory. No match or multiple matches produce a startup error with explicit-selection guidance; overlapping roots are not resolved by picking the nearest. `--workspace-root` (alias `--repo-root`) can supply the discovery directory. MCP never reads the `workspace use` CLI default, including when that default is invalid. Explicit `--workspace` bypasses discovery. Changing the default does not affect running servers. The host keeps one workspace for its lifetime. Run a separate server process to expose a different workspace. Standard output is reserved for MCP transport; diagnostic logging does not replace protocol responses.

Startup and `graph_stats` preserve missing or incompatible databases. Graph and memory tools initialize compatible storage on demand; initialization failures do not prevent later diagnostic calls. Read tools consistently advertise their read-only intent.

The checked-in `mcp-config.json` is a portable discovery example. `copilot.sh` uses it without selecting a provider or model; configure those in your client.

## Tools

| Tool | Purpose |
| --- | --- |
| `graph_stats` | Read graph coverage, database state, indexing timings, and recorded diagnostics; no node ID needed. |
| `semantic_search` | Find implementations from a behavior, concept or distinctive symbol; supports optional `project` (name or ID), `nodeTypes`, `includeMemories`, and `tags` filters. Limit defaults to 10. |
| `context` | Bounded immediate relationships for a node ID, with optional `limit` (1–50), `sections` subset, and truncation totals. |
| `trace_node` | Caller or callee navigation for a persisted integer node ID, with optional `maxDepth` (1–10; defaults to 3 for callers, 1 for callees), `includeStructural`, and `includedEdgeTypes`. |
| `get_inheritors` | Direct derived classes and interface implementers for a persisted integer node ID. |
| `attach_memory` | Attach Markdown content, optional tags, and an intent to a node; returns the created memory GUID and stale state. |
| `list_memories` | List persisted memories across the workspace with optional `intents`, `tags`, `staleOnly`, `searchText`, `limit`, and `offset` filters. |
| `delete_memory` | Delete a memory by GUID. |
| `get_memory` | Fetch one memory's full content. |
| `get_memories` | Fetch several memories in one request. |
| `ctx_execute` | Run an authorized noisy build, test or diagnostic command in the workspace root and return bounded matching excerpts. |

Refactoring and rename tools are not part of the version 1 surface.

Verify the workspace identity and language coverage with `graph_stats` before discovery. Its timestamps and ready state do not guarantee filesystem freshness. When the implementation location is unknown, search from the behavior being investigated, read the returned source, and request context or trace only when a relationship remains unresolved. Read known files directly and use text search for exact strings and exhaustive occurrence checks. Node IDs belong to this server's workspace. Use `edgeCategories: "All"` on context or trace to include memory metadata; the default `"Structural"` omits it. Fetch full notes separately. Caller traces default to depth three; CLI caller tracing defaults to immediate callers unless `--max-depth` is supplied. Static traces show dependencies, not runtime execution order.

`context`, `trace_node`, `get_inheritors`, `semantic_search`, `ctx_execute`, and the memory tools report expected application failures with `isError: true` and a readable explanation. Successful results retain the existing compact text. An executed child command's nonzero exit code remains part of the `ctx_execute` payload, distinct from failure to start or validate the tool request.

`ctx_execute` runs real local commands with the server process's filesystem permissions. It launches an executable without a shell, so shell operators are not interpreted. Quote individual arguments containing spaces; pipelines, redirection and chaining require an explicit shell. Its optional FTS query selects context windows, for example `error OR warning OR failed`; it is not a natural-language summarization instruction. An omitted or unmatched query returns a summary. Use ordinary source tools for small file reads. See [command execution](execute-command.md).

Use a concise behavior or concept as the semantic query, such as `conversation cancellation`, or a distinctive known symbol. Multiple terms can broaden lexical matching; avoid lists of unrelated concepts or guessed identifiers. Semantic search treats input as plain text. Symbol/path punctuation is safe, FTS operators and column selectors have no special meaning, and punctuation-only input returns no hits. The separate `ctx_execute` output query still accepts FTS syntax.

Install the [SharpSense plugin](skills-command.md) through Claude Code, Codex or Copilot for skills and an automatic MCP connection. The CLI must already be on the client's `PATH`, with a registered and analysed workspace. The client must inherit the same custom `SHARPSENSE_HOME` if used.
