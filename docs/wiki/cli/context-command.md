# Context

Context returns a compact snapshot of one indexed node's immediate callers, callees, and inheritance relationships:

```bash
sharpsense context --workspace product --node-id 42
sharpsense context --workspace product --node-id 42 --include-memories
```

Obtain the positive integer node ID from [search](search-command.md) or the UI. IDs are local to a workspace.

Output uses TOON and bounds related-node breadth. It separates functional and structural relationships where available; it does not traverse an entire dependency graph. Use [trace](trace-command.md) when you need a particular direction.

With `--include-memories`, attached notes appear as compact metadata, including their IDs and stale state. Fetch full content with [memory get](memory-command.md). The MCP equivalent is `context`, with an optional edge-category mask.
