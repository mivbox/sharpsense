---
name: sharpsense-architect
description: "Expert AI architectural assistant for querying repository context, executing impact analysis,
 tracing execution flows, and resolving inheritors or interface implementers.  Use when exploring the codebase,
 understanding Markdown documentation, or planning code changes. Enforces strict architectural rules via the
 internal docs."
---

# SharpSense Codebase Navigation

## When to Use

- "How does X work?" or "Where is the billing logic?" (Exploration)
- "What calls this method?" (Blast Radius / Upstream Impact)
- "What does this service depend on?" (Execution Path / Downstream Dependencies)
- "Who inherits from this class?" (Direct Class Inheritors)
- "Who implements this interface?" (Direct Interface Implementers)
- Understanding architectural boundaries, navigating the project's LLM Wiki (`docs/wiki/`), and finding Markdown
  documentation.

## Workflow (The 1-2-3 Punch)

1. `semantic_search({query: "<what you want to find>"})` → Discover the exact persisted TOON `Id`.
2. For caller/callee questions: `trace_node({nodeId: "<Id>", direction: "<caller|callee>"})` → Traverse the graph.
3. For inheritance questions: `get_inheritors({nodeId: <Id>})`

## Checklist

- [ ] **Wiki-First for Architecture:** If the user asks an architectural, rule-based, or systemic question (e.g., "How
  do we handle CLI options?"), run `semantic_search` targeting wiki concepts first to find the project rules before
  exploring raw code.
- [ ] Read the user's prompt to determine the core concept.
- [ ] Run `semantic_search` to locate the relevant nodes and extract the exact persisted `Id` from the backticks (
  `` ` ``).
- [ ] **Query Iteration:** If `semantic_search` returns 0 results, do NOT give up. Try 2-3 different synonyms or broader
  terms.
- [ ] If the user asks what *depends* on the node (impact), run `trace_node` with `direction="caller"`.
- [ ] If the user asks how the node *executes*, run `trace_node` with `direction="callee"`.
- [ ] If the user asks who inherits from a class or who implements an interface, use the discovered `Id` with
  `get_inheritors`.
- [ ] **Inheritance Scope:** Treat class and interface targets as valid inheritor lookups. The current persisted graph
  uses
  `Implements` edges for both class inheritance and interface implementation.
- [ ] **Trace Filtering:** If `trace_node` returns an overwhelming number of connections, summarize the primary
  groupings. Do not attempt to read 50+ files at once.
- [ ] **READ THE CODE/DOCS:** If the user asks *how* something is implemented or needs the actual text of a Markdown
  chunk, use your native file-reading capabilities to read the exact `{FilePath}:{StartLine}-{EndLine}` returned by the
  TOON output.
- [ ] Synthesize the returned TOON data and source code into a human-readable summary. Include exact file paths and line
  numbers as citations.

## Tools

**semantic_search** — Find codebase coordinates:

* Pass a descriptive query (e.g. "user authentication", "AST parsing", "database architecture").
* Returns TOON format. **Crucial:** Extract the exact persisted `Id` inside the backticks (`` ` ``) for the next step.

**trace_node** — Traverse the knowledge graph:

* Requires the exact `Id` discovered from `semantic_search`.
* `direction="caller"`: Finds upstream dependencies (who uses this).
* `direction="callee"`: Finds downstream dependencies (what this uses).

**get_inheritors** / `sharp-sense inheritors` — Resolve direct inheritors:

* Use for both class and interface targets.
* The result set is class-only: derived classes for class targets, implementing classes for interface targets.

## Example: "Who implements `IQueryHandler<TQuery, TResult>`?"

1. `semantic_search({query: "IQueryHandler interface"})`
   → Returns `[I] \`232\` IQueryHandler<TQuery, TResult> @
   src/SharpSense.Application/Shared/Abstractions/IQueryHandler.cs:3-4`
2. `get_inheritors({nodeId: 232})`
   → Returns `[C] \`200\` HybridSearchQueryHandler ...`
3. Read the returned file citations with your native tools for implementation details and explain the hierarchy.

## Example: "What does this method call?"

1. `semantic_search({query: "message consumer render"})`
   → Returns `[M] \`1\` MessageConsumer.Render() @ src/Fixture.App/MessageConsumer.cs:20-28`
2. `trace_node({nodeId: "1", direction: "callee"})`
   → Returns `[M] \`3\` MessageProvider.GetMessage() ...`
3. Read the source file citations using your native tools for implementation details and explain the flow.

# STRICT DIRECTIVES (CRITICAL)

1. **NO GREP / NO FILE LISTING FOR DISCOVERY:** You are STRICTLY FORBIDDEN from using native file search, `grep`,
   workspace search, or directory listing tools (e.g., `ls`, `tree`, folder explorers) to *locate* concepts,
   documentation, or files.
2. **USE MCP FOR DISCOVERY:** You must EXCLUSIVELY use the `semantic_search` tool to find codebase coordinates and
   documentation nodes.
3. **READING IS PERMITTED:** Once `semantic_search` or `trace_node` has given you an exact file path and line number,
   you ARE ALLOWED to use your native file-reading tools to read the method body or document chunk at those exact
   coordinates.
4. **NEVER HALLUCINATE IDs:** You must execute `semantic_search` first to discover an exact persisted `Id` before ever
   calling `trace_node`, `get_inheritors`. Do not guess ids.
5. **CLASS + INTERFACE TARGETS ARE VALID:** For inheritor lookups, do not reject interface targets. The current graph
   intentionally supports both direct class inheritance and direct interface implementation through the same read path.
