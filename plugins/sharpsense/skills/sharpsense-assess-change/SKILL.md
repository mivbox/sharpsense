---
name: sharpsense-assess-change
description: Assess a proposed code change using SharpSense callers, dependencies and source evidence to identify affected behavior and validation.
---

# Assess a change with SharpSense

Establish the proposed signature, behavior or lifecycle change before assessing its effects. Use the MCP server
for the intended workspace; use current source files when the graph is unavailable or incomplete.

## Inspect the affected paths

- Locate the target with `semantic_search`, or reuse its known persisted ID.
- Use `context` for immediate relationships. Use `trace_node` with `direction: "caller"` for upstream paths and
  `direction: "callee"` when downstream behavior matters. Use `get_inheritors` for relevant inheritance contracts.
- Read the target and relevant consumers, registrations, configuration and tests. Graph reachability identifies
  code to inspect; it does not establish that a caller will break or that an unlisted caller is safe.
- If coverage is uncertain, inspect `graph_stats` and the configured sources. An index timestamp is not a freshness
  guarantee. Reindex the explicit workspace when updating the index is within the requested work.

## Report useful findings

For each finding, identify the proposed change, the affected behavior, source evidence and a concrete validation
step. Distinguish confirmed incompatibility from a plausible impact needing verification. Consider contract changes,
state ownership, persistence and important execution paths; do not assign severity from relationship counts alone.

Example: an interface signature change can require edits to a consumer or implementation. A method-body cleanup
that preserves the contract needs different evidence, even when both changes have the same callers.

Fetch surfaced memories when relevant and compare them with source. Staleness alone is not grounds for deletion.
Memory curation should be part of the requested task; when replacing obsolete context, verify and attach the new
entry before deleting the old one. Follow the consuming repository's code and test conventions for any fixes.
