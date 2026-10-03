---
name: sharpsense-exploring
description: "Explain unfamiliar code and trace callers, dependencies or architecture with SharpSense. Use for code-navigation questions; use impact-analysis for a proposed change."
---

# Explore code with SharpSense

Answer the code question with graph navigation and verified source. A known file or simple text lookup can use
normal source tools directly; a graph is useful when relationships or ownership are unclear.

## Establish scope

Before using the graph, call `graph_stats({})` once unless this session already verified the MCP workspace. Check
its root, coverage, index outcome and diagnostics against the intended checkout. An index timestamp alone does not prove it matches
current source. If the binding is wrong or the graph is unavailable, use source tools and state the gap. Do not
create or reindex a workspace merely to answer a navigation question.

## Retrieve what the question needs

- Find an entry point with `semantic_search({query: "<distinctive symbol or concept>", limit: 5})`.
  Choose by path, project and symbol; use the returned numeric ID. Search is plain text, not FTS syntax.
  Multiple terms can broaden results, so refine a noisy query with a distinctive symbol instead of adding terms.
- Use `context({nodeId: 42})` for immediate relationships. Read the relevant implementations to establish
  ordering, branches and failure behavior. Stop when this evidence answers the question.
- Expand only an unresolved relationship: `trace_node({nodeId: "42", direction: "caller"})` for upstream consumers,
  or `direction: "callee"` for downstream dependencies. `get_inheritors({nodeId: 42})` finds direct implementations
  of a type or interface. These are static relationships, not runtime execution traces.
- For a question about rationale or known notes, request `edgeCategories: "All"` on context/trace to surface memory
  IDs. Fetch relevant contents with `get_memory`, or `get_memories` for several IDs. Verify stale notes against source;
  the stale flag means the attached symbol changed, not that its note is false. Discovery does not authorize memory writes.

Reuse established IDs only in the same workspace. Do not expand every search hit or repeat completed lookups.
Missing edges, truncated results and syntax-based TypeScript imports do not establish absence of behavior.
Use targeted source searches to resolve gaps. For a CLI fallback, inspect command help and select the workspace
explicitly; caller tracing defaults differ between CLI (one level) and MCP (three).

## Answer

Lead with the explanation or a short path through the code, citing source locations. Distinguish observed behavior
from inference and mention material workspace or coverage limits. Omit empty report sections and routine tool logs.
Delegate only independent investigations when delegation is authorized and useful; a single path needs no handoff.
