---
name: sharpsense-map-code
description: Locate symbols and explain code paths using a SharpSense workspace graph and the current source files.
---

# Map code with SharpSense

Use the SharpSense MCP server bound to the relevant workspace. If the server is unavailable, use the client's
normal file tools. When results are missing or surprising, `graph_stats` can explain indexed language coverage,
last-run diagnostics and recorded index state; it does not prove that files have not changed since indexing.

## Follow the question

1. Use `semantic_search` for an unknown symbol or concept. Reuse a persisted node ID when it is already known;
   numeric IDs belong to the selected workspace.
2. Use `context` for immediate relationships, `trace_node` for caller/callee paths, or `get_inheritors` for direct
   derived types. Call only the tools needed to answer the question.
3. Read the cited source files to verify implementation details. The graph is a navigation aid and can be stale
   or incomplete. For code changes, follow that repository's instructions and nearby maintained examples.
4. Explain the relevant path with file references, separating observed relationships from inferred behavior.
   State missing coverage when it limits the answer.

## Search syntax

Plain text becomes quoted prefix terms joined with OR: `message handler` searches for either prefix.
Use explicit FTS syntax when needed:

- `Message AND Handler` requires both terms; `Message OR Handler` accepts either.
- `Message NOT Test` excludes the second term from a matching expression.
- `"message handler"` matches the phrase without automatic prefix expansion.
- `FullyQualifiedName:Message*` scopes a prefix search to a supported column. Other supported columns are
  `DisplayName`, `SearchText` and `RelativeFilePath`.
- `NEAR(Message Handler)` uses FTS proximity matching.

## Historical context

Context and trace output can surface memory IDs. Fetch relevant text with `get_memory`, or use `get_memories`
for several IDs. Semantic memories are optional on context/trace calls; use their exposed `edgeCategories`
parameter when historical context is needed.

A stale flag means the attached symbol changed; verify the memory against current source before relying on it.
Create, remove or replace a memory when that is part of the requested work. Preserve accurate decisions and
invariants even when their attachment is stale. For a correction, attach the verified replacement before removing
an obsolete entry so a failed write does not discard the existing context.
