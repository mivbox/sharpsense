---
name: sharpsense-impact-analysis
description: "Locate unknown change targets with SharpSense semantic search before guessing filenames or symbols, then assess affected callers, implementations, and contracts. Use when a proposed change could affect other code or external consumers."
---

# Assess change impact with SharpSense

Identify the proposed signature, behavior, or lifecycle change and which contracts could be affected. For purely local edits whose observable contracts do not change, assess directly from source without graph expansion. When a globally unique symbol name is already known and you only need exhaustive direct references, `rg` alone is faster; use SharpSense impact analysis when the target is unfamiliar, heavily overloaded (for example `Handle` or `Execute`), or needs transitive callers and interface implementations.

## Contract-change checklist

If the workspace is not yet verified in this session, call `graph_stats({})` **in parallel** with your initial `semantic_search(...)` (or `context(...)` when the node ID is already known) and check the returned root/coverage before relying on hits. For pending edits, inspect the local Git diff directly — the graph reflects the last index, not uncommitted changes.

| Step | Tool call |
| --- | --- |
| Locate unknown target | `semantic_search({query: "<behavior or symbol>", nodeTypes: ["Class", "Interface", "Method"], limit: 5})` |
| Inspect direct callers, implementers & notes (with `@ path:line`) | `context({nodeId: 42, sections: ["Callers", "Implementers"], limit: 25, edgeCategories: "All"})` |
| Trace indirect upstream callers | `trace_node({nodeId: 42, direction: "caller", maxDepth: 3})` |
| Find interface/base implementations | `get_inheritors({nodeId: 42})` |
| Fetch surfaced decisions or invariants | `get_memories({memoryIds: [...]})` |

## Worked sequence

1. Resolve the target with `semantic_search` (in parallel with `graph_stats({})` on the first turn; reuse already-known IDs) and read its declaration with a bounded `view_range`.
2. Run `context({nodeId: 42, sections: ["Callers", "Implementers"], limit: 25, edgeCategories: "All"})` (and `get_inheritors({nodeId: 42})` in parallel for interfaces/base types). Use the returned `@ path:line` locations to read only the affected call sites; widen `limit` up to `50` or follow up with `rg` when totals show truncation (for example `callers (25 of 64)`).
3. Run `trace_node({nodeId: 42, direction: "caller", maxDepth: 3})` when transitive callers matter, fetch surfaced memory IDs with `get_memories`, and verify stale notes against current source.
4. Read each candidate consumer before classifying its consequence. Check serialization, persistence, DI registrations, cancellation, and external/cross-language boundaries when touched.

## Calibrate the conclusion

- A caller is a candidate to inspect, not proof of breakage.
- Zero indexed callers means unknown coverage, not unused — confirm with `rg` for dynamic dispatch, reflection, or cross-language consumers. TypeScript edges are syntax-based module imports; static traces do not prove execution order.
- Report confirmed incompatibilities, plausible effects, unresolved coverage (including truncation), and concrete validation. Separate tests run from tests recommended. Analysis does not authorize memory edits or commits.

For multi-hop blast-radius and CLI fallback examples, read [examples](references/examples.md).
