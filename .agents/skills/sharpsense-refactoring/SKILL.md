---
name: sharpsense-refactoring
description: "Use when the user wants to rename, extract, split, move, or restructure indexed code through SharpSense's Roslyn-backed semantic rename flow. Examples: \"Rename this method\", \"Refactor this class\", \"Move this logic\", \"Split this type\""
---

# Refactoring with SharpSense

## Core Idea

- `refactor_symbol` is the built-in Roslyn semantic rename tool.
- `nodeId` targets one persisted declaration; `newName` must be the new identifier only.
- For C# source nodes, `refactor_symbol` behaves like Rider `Ctrl+R, R` / Visual Studio `F2` and updates references across the loaded workspace.
- For indexed Markdown nodes, `refactor_symbol` performs a targeted local heading rename inside the persisted span only.
- Prefer syntax-first refactors. Use `context`, `trace_node`, and `get_inheritors` only when callers, contracts, or inheritance make the blast radius unclear.
- If you need the index/vector store to refresh after the write, keep `sharpsense analyze <target> --watch` running.

## When to Use

- "Rename this method safely"
- "Extract this logic into a new type"
- "Split this class"
- "Move this to a new file"
- Any task involving renaming, extracting, splitting, moving, or restructuring indexed C# code

## Default Workflow

```
1. semantic_search({query: "X"})                      → Find the exact declaration node id and file
2. Pick the declaration node you want to rename       → Prefer the symbol definition, not a caller
3. refactor_symbol({nodeId: <id>, newName: "..."})    → Apply the semantic rename
4. semantic_search({query: "new symbol or key text"}) → Confirm the updated shape in the index
6. Run affected tests
```

## Escalate Beyond a Single-Node Edit

Use `context(...)`, `trace_node(...)`, and `get_inheritors(...)` only when the rename affects more than one local declaration:

- Changing parameters, return types, accessibility, or base types
- Moving behavior across multiple existing nodes
- Updating public/external APIs
- Renaming inherited or interface-backed members

> If the index is stale, run `sharpsense analyze <target>` first. Keep `sharpsense analyze <target> --watch` running if you want refactor writes to refresh the index automatically.

## Checklists

### Rename Symbol

```
- [ ] semantic_search({query: "oldName"}) — find the declaration node id
- [ ] refactor_symbol({nodeId, newName}) — rename the declaration semantically
- [ ] semantic_search({query: "oldName"}) — verify stale symbol references are gone
- [ ] trace_node({nodeId: "<id>", direction: "caller"}) — use only if the blast radius is unclear
- [ ] get_inheritors({nodeId}) — use when interface or inheritance contracts are involved
- [ ] Run affected tests
```

### Extract Type / Move Logic

```
- [ ] semantic_search({query: target}) — locate the source node
- [ ] Create the new type/file with normal file edits
- [ ] refactor_symbol({nodeId, newName}) — use semantic rename for any declaration you are renaming during the extraction
- [ ] semantic_search({query: "new type name"}) — confirm the new shape is indexed
- [ ] context({nodeId}) / trace_node({nodeId: "<id>", direction: "caller"}) — only if callers/contracts changed
- [ ] Run affected tests
```

### Split Method / Class

```
- [ ] semantic_search({query: target}) — locate the exact node
- [ ] Create any new helpers/types with normal file edits
- [ ] refactor_symbol({nodeId, newName}) — use semantic rename if any existing declaration gets a new name
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

**get_inheritors** — optional inheritance check for classes and interfaces:

```
get_inheritors({nodeId: 42})
→ direct inheritors / implementers
```

**refactor_symbol** — semantically rename one persisted declaration:

```
refactor_symbol({nodeId: 42, newName: "AuthenticateUser"})
→ refactor_success: true
→ modified_files:
  - src/Auth/Validator.cs
  - src/Auth/LoginHandler.cs
```

## Risk Rules

| Risk Factor | Mitigation |
| --- | --- |
| Many callers (>5) | Use `trace_node(..., "caller")` after identifying the declaration |
| Cross-area refs | Use `context()` and trace only for affected contracts |
| String/dynamic refs | Use broader `semantic_search()` queries after each rename |
| External/public API | Version and deprecate properly |

## Example: Rename `ValidateUser` to `AuthenticateUser`

```
1. semantic_search({query: "ValidateUser"})
   → src/Auth/Validator.cs:
       - [M] `42` ValidateUser L10-42

2. refactor_symbol({nodeId: 42, newName: "AuthenticateUser"})
   → declaration and Roslyn-managed references updated together

3. semantic_search({query: "ValidateUser"})
   → verify no stale indexed hits remain after watch refresh

4. trace_node({nodeId: "42", direction: "caller"})
   → only if the remaining impact is unclear

5. Run affected tests
```
