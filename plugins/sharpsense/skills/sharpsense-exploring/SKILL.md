---
name: sharpsense-exploring
description: "Find unfamiliar implementations or docs with SharpSense semantic search before guessing filenames or symbols. Use during debugging, feature work, refactoring, or review when the location is unknown, and for code-relationship questions."
---

# Find and explore code with SharpSense

Use semantic search to locate unfamiliar implementations or documentation from a behavior or topic. Read known files directly and use `rg` for exact strings or exhaustive occurrence checks.

## Choose the right call

If the MCP workspace is not yet verified in this session, call `graph_stats({})` **in parallel** with your first `semantic_search(...)` call — check the returned root and coverage before relying on the hits. If the binding is wrong or unavailable, fall back to source tools and state the limitation once. Never create or reindex a workspace just to explore.

| Need | Tool call |
| --- | --- |
| Verify workspace binding once per session | `graph_stats({})` (in parallel with your first search, or read `sharpsense://workspace/stats`) |
| Locate unfamiliar code or UI components | `semantic_search({query: "source extraction failure", nodeTypes: ["Class", "Method", "Component"], limit: 5})` |
| Locate documentation by topic | `semantic_search({query: "workspace selection", nodeTypes: ["Document"], limit: 5})` |
| Scope search to one project or notes | `semantic_search({query: "retry policy", project: "MyApp.Application", includeMemories: true})` |
| Inspect immediate callers/callees/types (with `@ path:line`) | `context({nodeId: 42, sections: ["Callers", "Callees"], limit: 15})` |
| Trace multi-hop callers or component chains | `trace_node({nodeId: 42, direction: "caller", maxDepth: 3})` |
| Find direct implementations or subclasses | `get_inheritors({nodeId: 42})` |
| Retrieve attached rationale | Pass `edgeCategories: "All"` on `context`/`trace_node`, then `get_memories({memoryIds: [...]})` |

## Short workflows

- **Unfamiliar code or UI**: Call `graph_stats({})` and `semantic_search({query: "<concise behavior>", nodeTypes: ["Class", "Method", "Component"], limit: 5})` in parallel (add `project` when known). For multi-hop paths or component chains, prefer one `trace_node({nodeId, direction: "caller", maxDepth: 3})` over chaining serial 1-hop `context` calls. Then batch parallel `view` calls (using `view_range` around returned `file:line` spans for large files) and stop as soon as the core path and tests are verified.
- **Documentation question**: Call `graph_stats({})` and `semantic_search({query: "<topic>", nodeTypes: ["Document"], limit: 5})` in parallel, read the returned Markdown files directly, and stop once they answer the question (skip code tracing unless implementation details were requested).

## Guardrails

- Do not start unknown implementation discovery with guessed filenames or `rg --files` globs.
- `semantic_search` is plain text (no FTS operators or column selectors); keep queries focused on one concept and avoid repeating overlapping searches.
- Check `context` totals (for example `callers (10 of 27)`) and `truncated: true` on traces before concluding a list is complete.
- Read source before stating behavior; prefer bounded `view_range` reads on large test/source files when line ranges are already known.
- Missing edges, empty searches, truncation, and syntax-based TypeScript relationships do not prove absence. Static traces do not establish runtime order. Discovery does not authorize memory writes.

For multi-hop tracing, memory recall, and CLI fallback examples, read [workflows](references/workflows.md).
