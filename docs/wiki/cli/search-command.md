# Search

Search combines keyword and embedding rankings over indexed code and documentation:

```bash
sharpsense search "request validation" --workspace product
sharpsense search "request validation" --workspace product --toon
sharpsense search "request validation" --workspace product --include-memories
```

Empty queries return a readable validation error: exit code 1 in the CLI, HTTP 400 in the UI API, and `isError: true` in MCP.

JSON is the default output. `--toon` groups compact results by directory and file, using `[UI]` for TSX components. `--include-memories` lets attached memory content contribute to relevance.

Search input is plain text. The keyword side splits symbol/path separators, including `*` and `:`, ignores tokens
shorter than two characters, deduplicates case-insensitively, and ORs quoted prefix terms. `Payment AND Other`
searches those ordinary words; it is not an AND expression. Column selectors, quoted phrases and FTS operators
have no special query meaning. Punctuation-only input returns no hits without generating an embedding.
The original query text is retained for embeddings and result metadata.

Each hit provides a workspace-local node ID and source location. Use that ID with [context](context-command.md), [trace](trace-command.md), or [memories](memory-command.md), keeping the same workspace selected.

Index sources before searching. Search generates a query embedding, so `analyze --no-embeddings` is not a switch to disable the embedding runtime for all commands. [Doctor](doctor-command.md) reports embedding coverage and asset availability.

The [hybrid search pipeline](../architecture/hybrid-search-pipeline.md) ranks keyword and vector candidates in SQLite. The MCP `semantic_search` tool uses the same query boundary and exposes a result limit.
