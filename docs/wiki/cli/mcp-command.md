# MCP server

Start a stdio MCP server bound to a registered workspace:

```bash
sharpsense mcp --workspace product
```

Configure your MCP client to launch executable `sharpsense` with arguments `["mcp", "--workspace", "product"]`. Pass the same absolute `SHARPSENSE_HOME` environment value if the workspace uses a custom home. The workspace must be registered and analyzed before graph queries are useful.

The host keeps one workspace for its lifetime. Run a separate server process to expose a different workspace. Standard output is reserved for MCP transport; diagnostic logging does not replace protocol responses.

## Tools

| Tool | Purpose |
| --- | --- |
| `graph_stats` | Read graph coverage, database state, indexing timings, and recorded diagnostics; no node ID needed. |
| `semantic_search` | Hybrid keyword/vector search, with a result limit that defaults to 10. |
| `context` | Bounded immediate relationships for a node ID. |
| `trace_node` | Caller or callee navigation for a known node; caller traversal uses impact-analysis defaults. |
| `get_inheritors` | Direct derived classes and interface implementers. |
| `attach_memory` | Attach Markdown content, optional tags, and an intent to a node. |
| `delete_memory` | Delete a memory by GUID. |
| `get_memory` | Fetch one memory's full content. |
| `get_memories` | Fetch several memories in one request. |
| `ctx_execute` | Run a local command in the workspace repository and return bounded matching output excerpts. |

Refactoring and rename tools are not part of the version 1 surface.

Start with graph statistics if index state is uncertain. Search for a relevant node, request context, and trace only the necessary direction. Node IDs belong to this server's workspace. Context and trace include semantic-memory metadata only when the requested edge-category mask includes it; fetch full notes separately.

`ctx_execute` runs real local commands with the server process's filesystem permissions. It launches an executable without a shell, so shell operators are not interpreted. Its optional FTS query selects context windows; an omitted or unmatched query returns a summary. See [command execution](execute-command.md).

`--skills` exports the bundled [agent skills](skills-command.md) on startup. Install or configure skills according to the MCP client's conventions.
