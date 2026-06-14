---
name: sharpsense-refactoring
description: "Executes renaming of persisted declarations.
              Natively routes to semantic workspace-wide renaming for C# symbols,
              or local heading replacement for Markdown nodes.
              This tool ONLY performs renaming; it cannot split, move, or extract logic."
---

# Refactoring with SharpSense

## Operational Constraints

1. **ID Resolution**: NEVER guess `nodeId`. Always execute `semantic_search` to resolve the precise declaration ID
   before any mutation.
2. **Targeting**: Target the symbol definition or heading node directly. Do not target caller sites.
3. **Renaming Only**: `refactor_symbol` requires a single identifier string (e.g., `ExecuteTask` or
   `Setup Instructions`). Do not pass C# signatures or Markdown `#` prefixes.
4. **Markdown Constraints**: Markdown renaming is strictly limited to the local heading text within the persisted node
   span. It does not update external links to that heading.
5. **Post-Action Verification**: Immediately follow any write operation with `semantic_search` to ensure the vector
   index has refreshed.

## Escalation & Safety Protocol

- **C# Blast Radius**: If a C# symbol has **>5 callers**, you must execute `trace_node(direction: "caller")` or
  `context()` before renaming. Evaluate the architectural impact and external boundaries.
- **C# Inheritance**: For interfaces or abstract members, use `get_inheritors()` first to identify downstream impact.

## Memory Hygiene (CRITICAL after a rename)

`refactor_symbol` rewrites the declaration, which changes the live `BodyHash`. This ALWAYS invalidates any memory
previously attached to the renamed node (the memory's `IsStale` flag will flip to `true`).

Because of this, you MUST read the memory *before* you rename, so you do not permanently lose the context:

1. Read the memory via `get_memory(memoryId)`.
2. Perform the rename via `refactor_symbol`.
3. Call `delete_memory(memoryId)` to drop the now-stale record.
4. Re-attach the corrected intent via `attach_memory(nodeId, content, tags?)`.

Memories are immutable — there is no update verb; always delete + re-attach for corrections.

## Tool Sequence

1. **Discover**: `semantic_search` -> Retrieve `nodeId` and node type (C# vs Markdown).
2. **Analyze**: `context` -> Check structural risk (>5 callers? use `trace_node`) and note any attached memory IDs (
   `id + tags + stale`).
3. **Read Context**: `get_memory` -> Read the full markdown of any attached memories BEFORE mutating so you understand
   what intent needs to be carried over.
4. **Execute**: `refactor_symbol` -> Apply the rename.
5. **Verify & Maintain**: `semantic_search` -> Confirm new state. `delete_memory` + `attach_memory` to refresh the stale
   memories you read in Step 3.
