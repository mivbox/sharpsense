# Trace

Trace follows dependencies for an indexed node:

```bash
sharpsense trace 42 --workspace product --direction callee --toon
sharpsense trace 42 --workspace product --direction caller
sharpsense trace 42 --workspace product --include-memories --include-structural
```

The identifier can resolve an indexed node, but the numeric ID from search is the most direct choice. Fully qualified names can be ambiguous when separate projects declare the same name.

`callee` is the default direction. CLI caller tracing returns direct callers; it does not enumerate an unlimited transitive impact graph. Structural containment edges are excluded unless `--include-structural` is supplied. JSON is the default; `--toon` emits compact trace output.

`--include-memories` adds note metadata and stale indicators. Use [memory get](memory-command.md) to retrieve full content. With this flag, JSON is an object containing
`rootNode`, the related `nodes` array, and `memoriesByNodeId`, keyed by persisted node ID. Each memory contains
`id`, `intent`, `isStale`, and `tags`; note content is omitted. Without the flag, JSON remains a node array.

MCP `trace_node` shares query infrastructure, but its caller traversal uses impact-analysis defaults rather than the CLI's immediate-caller shortcut. UI trace controls can also set their own bounds. Treat each transport's parameters as part of the query.

See [context](context-command.md) for a bounded immediate overview and [inheritors](inheritors-command.md) for direct type relationships.

Traces show static graph relationships. They do not establish runtime execution order or cover every dynamic branch; verify relevant conditions and ordering in source.
