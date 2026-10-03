---
name: sharpsense-impact-analysis
description: "Assess a proposed code change or deletion with SharpSense: affected callers, contracts and validation. Use for change-risk questions, not routine edits or general code explanation."
---

# Assess change impact with SharpSense

Identify the proposed signature, behavior or lifecycle change. A caller is a candidate to inspect, not proof of
breakage: a contract-preserving cleanup differs from adding a required parameter.

Inspect the declaration and relevant uses first. For a purely local change whose contract and observable behavior
remain unchanged, answer from that source evidence; a graph inventory adds no evidence. Otherwise, use the graph
to find consumers or relationships that still need inspection.

## Find affected contracts

Before graph calls, use `graph_stats({})` once unless the MCP workspace is already verified in this session. Check
the root, index outcome and coverage against the intended checkout. Index timestamps are not revision guarantees.
For an unavailable or wrong workspace, use source evidence and report the limitation; do not silently analyze a different checkout.

1. Locate the target with `semantic_search({query: "<distinctive symbol>", limit: 5})`. Disambiguate by source path,
   project and declaration before using its persisted numeric ID. Queries are plain text, not FTS syntax.
2. Inspect `context({nodeId: 42})` and the target's source. Check the direct consumers against the proposed contract.
   `incoming.callers` identifies consumers; `incoming.implementers` identifies affected implementations.
3. Trace a changed member for its callers and a type/interface for its implementations. Reuse relationships
   already returned rather than querying them again. Use `trace_node({nodeId: "42", direction: "caller"})` for
   indirect upstream paths,
   `direction: "callee"` for changed downstream behavior, or `get_inheritors({nodeId: 42})` for type implementations.
   Verify relevant registrations, configuration and tests from source; stop expanding once the question is resolved.

For pending edits, inspect staged, unstaged and relevant untracked changes in the actual checkout. The workspace
root may span repositories; graph queries do not inspect a Git diff.

Read each affected contract before classifying its consequence. Consider serialization, persistence, ownership,
cancellation and external callers when the proposed change touches them. For relevant recorded decisions, request
`edgeCategories: "All"` on context/trace and fetch the surfaced IDs with `get_memory` or `get_memories`. Verify stale
notes; analysis alone does not authorize memory edits.

## Calibrate the conclusion

No indexed callers means unknown coverage, not unused or safe. Search source before concluding absence. Dynamic
dispatch and external or cross-language consumers may be missing; TypeScript import edges are module relationships.
Static traces do not establish execution order. Report truncation or failed queries as incomplete evidence.
For CLI fallback, inspect help and select the workspace explicitly; its default caller depth is one, versus MCP's three.

Report confirmed incompatibilities, plausible effects and unresolved coverage with source locations and concrete
validation. Separate tests actually run from tests recommended. Do not infer severity from counts or label every
caller as broken. Keep the answer proportional to the change; analysis adds no approval gate or authorization to commit.
