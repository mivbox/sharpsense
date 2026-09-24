---
name: sharpsense-exploring
description: "Standard workflow for architectural discovery and codebase exploration. Use this to trace execution flows, understand
project structure, find where logic lives, or retrieve node-specific historical context."
---

# Codebase Exploration Strategy

## Execution Checklist

Execute these steps systematically to build your architectural understanding:

- [ ] semantic_search for the concept you want to understand
- [ ] Extract the persisted id from the backticks
- [ ] context for the immediate breadth snapshot
- [ ] trace_node for deeper caller/callee flow when needed
- [ ] get_inheritors for hierarchy-specific questions
- [ ] get_memory for any surfaced memory IDs to read full historical context
- [ ] Read the cited source files or docs for implementation details

### Tool Syntax Guide: `semantic_search`

`semantic_search` utilizes native SQLite Reciprocal Rank Fusion (BM25 + Vector Cosine). Queries are split on
punctuation/whitespace and forwarded to FTS5 as **prefix terms** (e.g., `Mess` implicitly becomes `Mess*` and matches
`MessageProvider`).

**Use standard SQLite FTS5 operators to tighten results:**

* `A B`: AND (implicit) - matches nodes containing both tokens.
* `A OR B`: Either token.
* `NOT B`: Excludes token (e.g., `Message NOT Test`).
* `"A B"`: Exact phrase (Note: quotes disable prefix expansion).
* `col:A`: Column filter (e.g., `FullyQualifiedName:Message`).
* `NEAR(A B)`: Tokens within 10 positions of each other.

## Re-Integration (The Read Phase)

Once the exploration workflow is complete:

1. **The Read Phase:** If code modifications or deeper inspections are required, use standard file reading tools to
   inspect the exact line spans discovered during the tracing phase.
2. **Verify Stale Memories:** Read the associated source code to verify if any memories flagged `IsStale: true` are
   still accurate despite recent code changes.

## Memory Lifecycle & Hygiene

Memories are graph-native, immutable context nodes used to prevent future rabbit holes. You are responsible for
maintaining them after reading the file contents:

* **Create:** Call `attach_memory(nodeId, content, tags?)` to persist non-obvious behavior, workarounds, or invariants.
* **Resolve Stale:** If a stale memory is NO LONGER accurate, call `delete_memory(memoryId)`.
* **Update:** Memories are immutable. To correct one, you must `delete_memory(memoryId)` then `attach_memory(...)` with
  the revised context.
