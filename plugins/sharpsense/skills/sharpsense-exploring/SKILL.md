---
name: sharpsense-exploring
description: >-
  Explore unfamiliar code, explain architecture and trace dependencies with SharpSense.
  Use for questions such as "How does X work?", "Show me the auth flow", "Where does this logic live?",
  "What calls this function?" or "Why was this implemented this way?".
---

# Explore code with SharpSense

## Route substantial discovery to a subagent

For deep architectural tracing or discovery requiring several graph calls, delegate to a subagent when the
client supports it. Keep a simple symbol lookup in the main thread. The delegated worker executes the workflow
itself; it must not recursively delegate the same task. If delegation is unavailable, perform a bounded pass
with the available tools and state the limitation without claiming a subagent was used.

Give the worker the question, intended workspace and checkout, and this brief:

> Use the SharpSense graph to map the requested paths and retrieve relevant memories. Follow the workflow below.
> Read targeted source files when needed to verify behaviour. Return a concise map with supporting file and line
> references, observed branches and uncertainties. Do not edit source or curate memories during discovery.

## Bind the workspace

When MCP is available, call `graph_stats {}` once per connection before discovery, or reuse an already verified binding. Confirm the
workspace name/ID, root and language coverage match the question. Its `repositoryRoot` response field names the
workspace root, which may contain several repositories. Numeric node IDs belong only to that workspace.

MCP is bound when it starts: explicit `--workspace` wins, otherwise exactly one registered root must contain
its launch directory. It never uses the `workspace use` CLI default. A session cannot switch workspaces with a
tool argument. If the binding is wrong, use the correct server or explicit CLI workspace; ask only when the
intended workspace is unclear.

Record available indexing timestamps and diagnostics. `databaseState: ready` and a successful index timestamp
show recorded state, not proof that current files match the index. If relevant sources are missing or changed,
use source evidence and explain the coverage limit. Reindex only when authorised for the task:

```bash
sharpsense analyze --workspace <name-or-id>
```

## Follow the question

1. `semantic_search({query: "<symbol or concept>"})` finds candidate nodes. Choose by symbol, file and project;
   extract the persisted numeric ID. Reuse a known ID only within the verified workspace.
2. `context({nodeId: 42})` shows immediate relationships. Request `edgeCategories: "All"` when memories matter;
   the default `"Structural"` omits semantic-memory metadata.
3. `trace_node({nodeId: "42", direction: "callee"})` explores downstream dependencies.
   Use `direction: "caller"` when upstream entry points matter. Set `edgeCategories: "All"` for memory metadata.
4. `get_inheritors({nodeId: 42})` finds direct derived types or interface implementers when relevant.
5. Fetch surfaced notes with `get_memory({memoryId: "<guid>"})`, or `get_memories({memoryIds: ["<guid>"]})`.
   Context and trace expose IDs, tags and stale flags rather than full memory text.
6. Read the relevant implementations, registrations and consumers. Verify ordering, conditions, asynchronous
   work and failure handling from source. A static dependency trace is not a runtime execution trace.

Use only the calls needed for the question. Search takes plain text: `payment handler` searches quoted prefix
terms joined with OR. Punctuation such as `*`, `:`, dots and path separators is handled safely; FTS operators,
column selectors and quoted phrases have no special query meaning. Tokens shorter than two characters are
ignored; punctuation-only input returns no hits. Use a more specific symbol term or inspect paths and projects to narrow candidates; adding OR terms can broaden results.

If MCP is unavailable, use explicit CLI selection or ordinary source tools:

```bash
sharpsense search "payment handler" --workspace commerce --toon
sharpsense context --node-id 42 --workspace commerce --include-memories --toon
sharpsense trace 42 --direction callee --workspace commerce --include-memories --toon
```

CLI caller tracing defaults to immediate callers; MCP caller tracing defaults to depth three. Do not claim
identical coverage when falling back between them. Use current source to fill gaps in either result.

## Return a compact, evidenced map

Use the relevant parts of this handoff; omit paths that were not observed:

```text
Workspace: name, ID, root; recorded index state and coverage limits
Primary path: Entry -> Handler -> Dependency [file:line references]
Async/event path: observed trigger -> consumer [references]
Error/fallback path: condition -> handler [references]
Memories: relevant decisions, IDs and stale flags; verified or still uncertain
File targets: specific paths and line spans for follow-up
Summary: observed behaviour, inferences and unresolved questions
```

A map need not include every possible branch. The parent agent checks the cited source before implementation
or a strong behavioural claim, and verifies stale notes before relying on them. Include the workspace and
material coverage limits in the user-facing explanation.

Example: for "How does checkout work?", bind the commerce workspace, search `checkout`, inspect the selected
handler's context and callees, then read its implementation and error handling. Report the validated path and
any unresolved dynamic dispatch rather than presenting graph order as execution order.

## Memory hygiene

A stale flag means the attached symbol changed; it does not prove the note is wrong. Preserve accurate
invariants and decisions. Create or curate memories only when included in the requested work. After verifying
a correction against source, attach the replacement before deleting the obsolete note. Memories are immutable;
there is no update tool. Discovery alone does not authorise memory writes.
