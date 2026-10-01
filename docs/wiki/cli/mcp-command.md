# MCP server

Start a stdio MCP server bound to a registered workspace:

```bash
sharpsense mcp                      # Discover one workspace containing this directory
sharpsense mcp --workspace product  # Bind explicitly
```

Configure your MCP client to launch executable `sharpsense` with arguments `["mcp", "--workspace", "product"]`. Pass the same absolute `SHARPSENSE_HOME` environment value if the workspace uses a custom home. The workspace must be registered and analyzed before graph queries are useful.

Without `--workspace`, MCP selects the single registered root containing its launch directory. No match or multiple matches produce a startup error with explicit-selection guidance; overlapping roots are not resolved by picking the nearest. `--workspace-root` (alias `--repo-root`) can supply the discovery directory. MCP never reads the `workspace use` CLI default, including when that default is invalid. Explicit `--workspace` bypasses discovery. Changing the default does not affect running servers. The host keeps one workspace for its lifetime. Run a separate server process to expose a different workspace. Standard output is reserved for MCP transport; diagnostic logging does not replace protocol responses.

## Tools

| Tool | Purpose |
| --- | --- |
| `graph_stats` | Read graph coverage, database state, indexing timings, and recorded diagnostics; no node ID needed. |
| `semantic_search` | Hybrid keyword/vector search from plain text, with a result limit that defaults to 10. |
| `context` | Bounded immediate relationships for a node ID. |
| `trace_node` | Caller or callee navigation for a known node; caller traversal uses impact-analysis defaults. |
| `get_inheritors` | Direct derived classes and interface implementers. |
| `attach_memory` | Attach Markdown content, optional tags, and an intent to a node. |
| `delete_memory` | Delete a memory by GUID. |
| `get_memory` | Fetch one memory's full content. |
| `get_memories` | Fetch several memories in one request. |
| `ctx_execute` | Run a local command in the workspace root and return bounded matching output excerpts. |

Refactoring and rename tools are not part of the version 1 surface.

Verify the workspace identity and language coverage with `graph_stats` before discovery. Its timestamps and ready state do not guarantee filesystem freshness. Search for a relevant node, request context, and trace only the necessary direction. Node IDs belong to this server's workspace. Use `edgeCategories: "All"` on context or trace to include memory metadata; the default `"Structural"` omits it. Fetch full notes separately. Caller traces default to depth three; CLI caller tracing defaults to immediate callers. Static traces show dependencies, not runtime execution order.

`context`, `semantic_search`, `ctx_execute`, and the memory tools report expected application failures with `isError: true` and a readable explanation. Successful results retain the existing compact text. An executed child command's nonzero exit code remains part of the `ctx_execute` payload, distinct from failure to start or validate the tool request.

`ctx_execute` runs real local commands with the server process's filesystem permissions. It launches an executable without a shell, so shell operators are not interpreted. Its optional FTS query selects context windows; an omitted or unmatched query returns a summary. See [command execution](execute-command.md).

Semantic search treats input as plain text. Symbol/path punctuation is safe, FTS operators and column selectors have no special meaning, and punctuation-only input returns no hits. The separate `ctx_execute` output query still accepts FTS syntax.

Install the [SharpSense plugin](skills-command.md) through Codex or Copilot for skills and an automatic MCP connection. The CLI must already be on the client's `PATH`, with a registered and analysed workspace. The client must inherit the same custom `SHARPSENSE_HOME` if used.
