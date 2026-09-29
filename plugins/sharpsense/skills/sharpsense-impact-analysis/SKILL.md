---
name: sharpsense-impact-analysis
description: >-
  Assess the effects of a proposed code change with SharpSense and current source evidence.
  Use for "Is it safe to change X?", "What depends on this?", "What will break?", or a dependency
  review before a non-trivial edit or commit.
---

# Assess change impact with SharpSense

Establish the proposed signature, behaviour or lifecycle change before assessing its effects. Direct callers
are candidates to inspect, not proof of breakage. An interface change and a contract-preserving method cleanup
can have the same callers but different consequences.

## Workflow

1. `semantic_search({query: "<target symbol or concept>"})` finds candidates. Disambiguate by path, project
   and current source before using the persisted ID. Search is plain text, not raw FTS syntax.
2. `context({nodeId: 42})` shows immediate callers, callees and inheritance. Use `edgeCategories: "All"`
   when relevant memory metadata is needed; the default `"Structural"` omits it.
3. `trace_node({nodeId: "42", direction: "caller"})` expands upstream dependencies. Inspect direct callers
   first, then relevant indirect paths. MCP caller traversal defaults to depth three.
4. Use `trace_node({nodeId: "42", direction: "callee"})` when downstream behaviour matters, and
   `get_inheritors({nodeId: 42})` for affected base/interface contracts and implementations.
5. Read the target, consumers, registrations, configuration and relevant tests. Verify the proposed change
   against actual use: signatures, serialisation, persistence, state ownership, cancellation and failure paths
   as applicable. Fetch relevant notes with `get_memory` or `get_memories` and verify them against source.
6. Report confirmed incompatibilities, potential effects and unresolved coverage, with file/line evidence
   and specific validation steps. Run focused checks when within the requested work; separate checks run
   from recommendations.

If MCP is unavailable, use explicit CLI selection or source tools:

## Interpret the evidence

| Signal                        | What to check                                                           |
|-------------------------------|-------------------------------------------------------------------------|
| `incoming.callers`            | Direct consumers and the contract each relies on.                       |
| `incoming.implementers`       | Implementations affected by a hierarchy or contract change.             |
| `outgoing.callees`            | Dependencies whose behaviour or lifecycle the target relies on.         |
| `outgoing.inherits`           | Base/interface contracts that constrain the change.                     |
| No callers or missing results | Coverage is inconclusive; search source before claiming unused or safe. |

Dynamic dispatch, reflection in the indexed product, external consumers and cross-language calls may be absent
from the graph. TypeScript imports are module relationships rather than compiler-resolved function calls.
Truncation, missing sources or tool failures reduce confidence; report them instead of treating empty results as
no impact. A static trace does not establish execution order or every runtime branch.

Do not derive severity solely from relationship counts or label every direct caller "WILL BREAK". Use the
actual contract change, evidence and consequence. This analysis informs the user's requested work; it does not
create an extra approval gate or authorise commits, pushes or unrelated edits.

Example: changing `ValidateUser` to accept a new parameter may require updates to a login handler and a token
refresh path. Confirm both from source, identify incompatible calls, then recommend or run their focused tests.
If no callers are indexed, report the uncertainty and search source before concluding the method is unused.

## Report and memory hygiene

For each finding include the affected behaviour, evidence, confidence (confirmed, potential or unknown), and a concrete
check. A small report with verified findings is more useful than an unsupported risk score.

Stale notes mean their attached symbol changed, not that the note must be deleted. Memory curation belongs only
in the requested scope. Preserve accurate decisions; for a verified correction, attach the replacement before
deleting the old immutable note. Follow the repository's conventions for any authorised fixes.
