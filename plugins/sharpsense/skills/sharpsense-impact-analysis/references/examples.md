# Impact-analysis examples and CLI fallback

## Changing a repository or handler method

```json
{"query": "HybridSearchQueryHandler", "nodeTypes": ["Class", "Method"], "limit": 5}
{"nodeId": 42, "sections": ["Callers", "Implementers"], "limit": 25, "edgeCategories": "All"}
{"nodeId": 42, "direction": "caller", "maxDepth": 3, "edgeCategories": "All"}
```

1. Check the `callers (M of N)` total in `context`. If `N > M`, raise `limit` (up to `50`) or run a targeted `rg` for exact symbol occurrences.
2. Inspect each direct caller and transport adapter (CLI, MCP, HTTP endpoint) to verify parameter defaults, error mapping, and OpenAPI/client contracts.
3. If `trace_node` surfaces attached memories, batch-fetch them with `get_memories` to catch invariants (such as transaction boundaries or error-code contracts) before editing.

## Changing an interface or base class

1. Call `get_inheritors({nodeId: 18})` to list direct implementations and subclasses (including test doubles in indexed test projects if selected in the workspace).
2. Also inspect DI registration files (service collection extensions) and mocks in unindexed test projects with `rg`.

## CLI fallback

When MCP is unavailable, run with explicit `--workspace`:

```bash
sharpsense context --node-id 42 --workspace my-app --section Callers --section Implementers --limit 25 --include-memories --toon
sharpsense trace 42 --direction caller --max-depth 3 --workspace my-app --include-memories --toon
sharpsense inheritors 42 --workspace my-app --toon
```

Remember: CLI `trace --direction caller` defaults to `--max-depth 1`; pass `--max-depth 3` to match MCP's caller default.
