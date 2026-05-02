---
name: sharpsense-refactoring
description: "Use when the user wants to replace, rename, extract, split, move, or restructure indexed C# syntax nodes through SharpSense's Roslyn-backed refactor flow. Examples: \"Rename this method\", \"Refactor this class\", \"Move this logic\", \"Split this type\""
---

# Refactoring with SharpSense

## Core Idea

- `refactor_node` is a Roslyn-backed exact-node replacement.
- `nodeId` is a targeting handle for one persisted syntax node; the replacement must be the full new source for that node.
- Prefer syntax-first refactors. Use `context` and `trace_node` only when the change affects callers, contracts, inheritance, or other nodes.
- `refactor_node` does not automatically rename every caller or create new files for you. Use normal file edits for new files/types, then use `refactor_node` to update existing indexed nodes.
- If you need the index/vector store to refresh after the write, keep `sharpsense analyze <target> --watch` running.

## When to Use

- "Rename this method safely"
- "Extract this logic into a new type"
- "Split this class"
- "Move this to a new file"
- Any task involving renaming, extracting, splitting, moving, or restructuring indexed C# code

## Default Workflow

```
1. semantic_search({query: "X"})                     → Find the exact node id and file
2. Pick the target node you want to replace          → Declaration or caller, one node at a time
3. Write the full replacement source for that node   → The tool replaces the full persisted span
4. refactor_node({nodeId: <id>, newCode: "..."})     → Apply the Roslyn-backed edit
5. semantic_search({query: "new symbol or key text"}) → Confirm the updated shape in the index
6. Run affected tests
```

## Escalate Beyond a Single-Node Edit

Use `context(...)` and `trace_node(...)` only when the refactor changes more than the body of one node:

- Renaming a symbol used elsewhere
- Changing parameters, return types, accessibility, or base types
- Moving behavior across multiple existing nodes
- Updating public/external APIs

> If the index is stale, run `sharpsense analyze <target>` first. Keep `sharpsense analyze <target> --watch` running if you want refactor writes to refresh the index automatically.

## Checklists

### Rename Symbol

```
- [ ] semantic_search({query: "oldName"}) — find the declaration node id
- [ ] refactor_node({nodeId, newCode}) — update the declaration in place
- [ ] semantic_search({query: "oldName"}) — verify stale symbol references are gone
- [ ] refactor_node({nodeId, newCode}) — update remaining caller/declaration nodes one at a time
- [ ] trace_node({nodeId: "<id>", direction: "caller"}) — use only if the blast radius is unclear
- [ ] Run affected tests
```

### Extract Type / Move Logic

```
- [ ] semantic_search({query: target}) — locate the source node
- [ ] Create the new type/file with normal file edits
- [ ] refactor_node({nodeId, newCode}) — replace the original node with the extracted shape
- [ ] semantic_search({query: "new type name"}) — confirm the new shape is indexed
- [ ] context({nodeId}) / trace_node({nodeId: "<id>", direction: "caller"}) — only if callers/contracts changed
- [ ] Run affected tests
```

### Split Method / Class

```
- [ ] semantic_search({query: target}) — locate the exact node
- [ ] Create any new helpers/types with normal file edits
- [ ] refactor_node({nodeId, newCode}) — replace the original node span
- [ ] semantic_search({query: "new helper name"}) — verify the reshaped code is indexed
- [ ] trace_node({nodeId: "<id>", direction: "caller"}) / trace_node({nodeId: "<id>", direction: "callee"}) — only if the split changes cross-node behavior
- [ ] Run affected tests
```

## Tools

**semantic_search** — find the node ids you must change:

```
semantic_search({query: "validateUser"})
→ src/Auth/:
    Validator.cs:
      - [M] `42` ValidateUser L10-42
```

**context** — optional contract snapshot for the target node:

```
context({nodeId: 42})
→ node / incoming / outgoing breadth in compressed TOON
```

**trace_node** — optional blast-radius check when a change affects other nodes:

```
trace_node({nodeId: "42", direction: "caller"})
→ upstream callers

trace_node({nodeId: "42", direction: "callee"})
→ downstream callees
```

**refactor_node** — replace one persisted node source span through Roslyn:

```
refactor_node({nodeId: 42, newCode: "public string AuthenticateUser() { ... }"})
→ refactor_success: true
→ modified_files:
  - src/Auth/Validator.cs
```

## Risk Rules

| Risk Factor | Mitigation |
| --- | --- |
| Many callers (>5) | Use `trace_node(..., "caller")` after identifying the declaration |
| Cross-area refs | Use `context()` and trace only for affected contracts |
| String/dynamic refs | Use broader `semantic_search()` queries after each replacement |
| External/public API | Version and deprecate properly |

## Example: Rename `ValidateUser` to `AuthenticateUser`

```
1. semantic_search({query: "ValidateUser"})
   → src/Auth/Validator.cs:
       - [M] `42` ValidateUser L10-42

2. refactor_node({nodeId: 42, newCode: "public string AuthenticateUser() { ... }"})
   → declaration updated in place

3. semantic_search({query: "ValidateUser"})
   → remaining callers/declarations still using the old symbol

4. refactor_node({nodeId: <callerId>, newCode: "AuthenticateUser(...)"})
   → caller updated in place

5. trace_node({nodeId: "42", direction: "caller"})
   → only if the remaining impact is unclear

6. Run affected tests
```
