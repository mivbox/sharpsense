# Context

Context returns a compact snapshot of one indexed node's immediate callers, callees, and inheritance relationships:

```bash
sharpsense context --workspace product --node-id 42
sharpsense context --workspace product --node-id 42 --toon
sharpsense context --workspace product --node-id 42 --include-memories
```

Obtain the positive integer node ID from [search](search-command.md) or the UI. IDs are local to a workspace.

JSON is the default output; `--toon` selects the existing compact TOON format. Related-node breadth is bounded. It separates functional and structural relationships where available; it does not traverse an entire dependency graph. Use [trace](trace-command.md) when you need a particular direction.

With `--include-memories`, attached notes appear as metadata, including their IDs, intents, tags and stale state. JSON adds a `memories` array; TOON includes the compact memory summary. Neither format embeds full note content. Fetch full content with [memory get](memory-command.md). The MCP equivalent is `context`, with an optional edge-category mask.

A missing node produces a concise failure message on stderr and CLI exit code 1, leaving stdout empty. The HTTP context tool returns 404. MCP returns the failure text with `isError: true`; successful MCP calls retain their TOON text. Invalid node IDs are rejected; cancellation and unexpected storage failures retain their existing handling.
