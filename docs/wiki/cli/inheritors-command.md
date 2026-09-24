# Inheritors

Find direct derived classes or interface implementers for an indexed node:

```bash
sharpsense inheritors 42 --workspace product
sharpsense inheritors 42 --workspace product --toon
```

The positional argument is a positive integer node ID from the same workspace. JSON is the default output; `--toon` produces compact results. This command reports direct relationships present in the graph, not all transitive descendants.

The CLI, MCP `get_inheritors` tool, and UI use the same application query. Relationship coverage depends on the [language extractor](../index.md#architecture). Use [search](search-command.md) to identify a base type or interface first.
