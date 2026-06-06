---
name: sharpsense-impact-analysis
description: "Use when the user wants to know what will break if they change something, or needs safety analysis before editing code. Examples: \"Is it safe to change X?\", \"What depends on this?\", \"What will break?\""
---

# Impact Analysis with SharpSense

## When to Use

- "Is it safe to change this function?"
- "What will break if I modify X?"
- "Show me the blast radius"
- "Who uses this code?"
- Before making non-trivial code changes
- Before committing — to understand what your changes affect

## Workflow

```
1. semantic_search({query: "<target symbol or concept>"})         → Discover the persisted node id
2. context({nodeId: <id>})                                        → Inspect immediate callers, implementers, callees, and inherits
3. trace_node({nodeId: "<id>", direction: "caller"})             → Expand the upstream blast radius
4. trace_node({nodeId: "<id>", direction: "callee"})             → Check downstream execution flow if behaviour may shift
5. get_inheritors({nodeId: <id>})                                 → Check hierarchy-specific impact when classes/interfaces are involved
6. Assess risk and report the likely breakage surface
```

> If the index is stale or missing the changed area, run `sharpsense analyze <target>` in the terminal first.

## Memory hygiene

Whenever the impact analysis confirms a non-trivial behaviour, invariant, or convention on the target node,
persist it for future sessions via the MCP `attach_memory(nodeId, content, tags?)` tool. Conversely, if the
user retracts an intent, the body changes enough to make a prior memory obsolete, or the tool's `IsStale`
flag flips to `true` on a re-parse, call `delete_memory(memoryId)` to drop the stale record. Memories are
immutable once attached — there is no update verb; always delete + re-attach for corrections.

`context` and `trace` surface memories inline as `id + tags + stale` only — never as content. To read the
full markdown, call `get_memory(memoryId)`.

## Checklist

```
- [ ] semantic_search for the target symbol or concept
- [ ] Extract the persisted id from the backticks
- [ ] context for the d=1 breadth snapshot
- [ ] trace_node(direction: "caller") to map direct blast radius
- [ ] trace_node(direction: "callee") when behavioural fallout matters
- [ ] get_inheritors when inheritance or interfaces are involved
- [ ] Assess and report the risk level
```

## Understanding Output

| Signal | Risk Level | Meaning |
| --- | --- | --- |
| `incoming.callers` | **WILL BREAK** | Direct upstream callers / dependents |
| `incoming.implementers` | HIGH | Direct hierarchy dependents |
| `outgoing.callees` | MEDIUM | Behaviour touched downstream |
| `outgoing.inherits` | MEDIUM | Base/interface contract dependency |

## Risk Assessment

| Affected | Risk |
| --- | --- |
| 1-3 direct relationships | LOW |
| 4-10 direct + traced relationships | MEDIUM |
| 10+ relationships or multiple critical flows | HIGH |
| Auth, payments, startup composition | CRITICAL |

## Tools

**context** — immediate blast radius snapshot:

```
context({nodeId: 42})
→ incoming.callers / incoming.implementers / outgoing.callees / outgoing.inherits
```

**trace_node** — deeper blast radius or execution path:

```
trace_node({nodeId: "42", direction: "caller"})
→ upstream callers

trace_node({nodeId: "42", direction: "callee"})
→ downstream callees
```

## Example: "What breaks if I change validateUser?"

```
1. semantic_search({query: "validate user"})
   → src/Auth/UserValidator.cs:
       - [M] `84` ValidateUser L15-39
2. context({nodeId: 84})
   → immediate callers, callees, and hierarchy breadth
3. trace_node({nodeId: "84", direction: "caller"})
   → login handler, API middleware, token refresh path
4. Risk: direct callers + auth path touched = HIGH
```
