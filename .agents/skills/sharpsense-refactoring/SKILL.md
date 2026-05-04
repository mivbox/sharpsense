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

## **Escalation & Safety Protocol**

- **C# Blast Radius**: If a C# symbol has **>5 callers**, you must execute `trace_node(direction: "caller")` or
  `context()` before renaming. Evaluate the architectural impact and external boundaries.
- **C# Inheritance**: For interfaces or abstract members, use `get_inheritors()` first to identify downstream impact.

## **Tool Sequence**

1. **Discover**: `semantic_search` -> Retrieve `nodeId` and node type (C# vs Markdown).
2. **Analyze**: (If C# and >five callers) `trace_node` / `context` -> Evaluate risk.
3. **Execute**: `refactor_symbol` -> Apply to rename.
4. **Verify**: `semantic_search` -> Confirm state.
