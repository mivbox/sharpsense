---
name: sharpsense-exploring
description: "Use when the user asks how code works, wants to understand architecture, trace execution flows, or explore unfamiliar parts of the codebase. Examples: \"How does X work?\", \"What calls this function?\", \"Show me the auth flow\""
---

# Exploring Codebases with SharpSense

## When to Use

- "How does authentication work?"
- "What's the project structure?"
- "Show me the main components"
- "Where is the database logic?"
- Understanding code you haven't seen before

## Workflow

```
1. semantic_search({query: "<what you want to understand>"})      → Discover relevant nodes and persisted ids
2. context({nodeId: <id>})                                        → Load immediate callers, callees, and hierarchy breadth
3. trace_node({nodeId: "<id>", direction: "callee"})             → Follow downstream execution flow
4. trace_node({nodeId: "<id>", direction: "caller"})             → Find upstream entry points if needed
5. get_inheritors({nodeId: <id>})                                 → Expand inheritance / implementation details when relevant
6. Read cited source files or wiki docs for implementation detail
```

> If the index is stale or missing the target area, run `sharpsense analyze <target>` in the terminal first.

## Checklist

```
- [ ] semantic_search for the concept you want to understand
- [ ] Extract the persisted id from the backticks
- [ ] context for the immediate breadth snapshot
- [ ] trace_node for deeper caller/callee flow when needed
- [ ] get_inheritors for hierarchy-specific questions
- [ ] Read the cited source files or docs for implementation details
```

## Tools

**semantic_search** — find relevant nodes for a concept:

```
semantic_search({query: "payment processing"})
→ src/Payments/Processor.cs:
    - [M] `42` ProcessPayment L10-42
```

**context** — instant architectural dashboard for a node:

```
context({nodeId: 42})
→ node / incoming / outgoing breadth in compressed TOON
```

**trace_node** — traverse deeper execution flow:

```
trace_node({nodeId: "42", direction: "callee"})
→ downstream callees

trace_node({nodeId: "42", direction: "caller"})
→ upstream callers
```

**get_inheritors** — resolve derived classes or interface implementers:

```
get_inheritors({nodeId: 232})
→ direct inheritors / implementers
```

## Example: "How does payment processing work?"

```
1. semantic_search({query: "payment processing"})
   → src/Payments/Processor.cs:
       - [M] `42` ProcessPayment L10-42
2. context({nodeId: 42})
   → callers, callees, and hierarchy breadth for ProcessPayment
3. trace_node({nodeId: "42", direction: "callee"})
   → validateCard, chargeGateway, persistTransaction
4. Read the cited files for the implementation details
```
