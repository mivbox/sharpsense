---
title: "Memory Command"
type: cli
tags: [spectre, memory, semantic, implemented]
created: 2026-06-06
updated: 2026-06-06
confidence: high
---

## Command

`sharp-sense memory <action> [options]` exposes the persistent semantic-memory layer to the terminal. The same
handlers back the MCP `attach_memory` and `delete_memory` tools, so the CLI and the MCP surface stay in lock-step.

## Actions

| Action  | Example                                              | Purpose                                                                                  |
|---------|------------------------------------------------------|------------------------------------------------------------------------------------------|
| `add`   | `sharpsense memory add --node-id 42 --content "..." --intent Invariant` | Attach a markdown memory to a code node, with optional `--tag` and `--intent` flags. |
| `list`  | `sharpsense memory list --node-id 42 --intent-filter Invariant` | List every memory currently attached to a code node, optionally filtered by intent. |
| `get`   | `sharpsense memory get --memory-id <guid>` or `--memory-ids g1,g2` | Fetch the full markdown content of one or many memories in one round-trip. |
| `remove`| `sharpsense memory remove --memory-id <guid>`        | Remove a previously attached memory by its persistent Guid.                               |

## Options

| Setting        | Source                          | Purpose                                                                |
|----------------|---------------------------------|------------------------------------------------------------------------|
| `Action`       | positional `<action>`           | `add`, `list`, or `remove`.                                            |
| `NodeId`       | `--node-id <NODE_ID>`           | Required for `add` and `list`. The persisted integer handle from search. |
| `MemoryId`     | `--memory-id <MEMORY_ID>`       | Required for `remove`. The persistent `Guid` of the memory.            |
| `Content`      | `--content <CONTENT>`           | Required for `add`. Markdown memory payload.                            |
| `Tags`         | `--tag <TAG>` (repeatable)      | Optional for `add`. Lowercased, de-duplicated, sorted.                 |
| `RepositoryRoot` | `--repo-root <path>`          | Override the repository root used for the persisted index.              |
| `IsVerbose`    | `-v\|--verbose`                 | Enable verbose command-host logging.                                    |

## Exit Codes

| Code | Meaning                                                                                     |
|------|---------------------------------------------------------------------------------------------|
| `0`  | Success — memory was attached, listed, or removed.                                          |
| `1`  | Operation failed (unknown node, empty content, unknown Guid, etc.) — the message is printed. |

## Lifecycle & Cascade

- The CLI/MCP boundary uses the persisted integer `NodeId`; the FQDN is resolved internally and never crosses
  the tool boundary.
- Memories are immutable once attached. The only write verbs are `add` (attach) and `remove` (delete).
- The `IsStale` flag on a memory flips to `true` automatically when the target method's `BodyHash` changes
  during a re-parse. The agent should then `remove` the stale memory and re-`add` the corrected one.
- When a code node is removed from the index (e.g. the underlying file is deleted and the workspace is
  re-parsed), the SQLite cascade foreign key on `MemoryNodeRecord.TargetFullyQualifiedName` automatically
  removes the matching memories. See [[persistence/sqlite-schema]].

## Composition

The command follows the standard CLI composition recipe (see [[architecture/host-composition]]):

```
services.AddRepositoryWorkspace(root);
services.AddMemory();
services.AddMemoryInfrastructure();
services.AddPersistence();
```

`Program.CommandApp.cs` wires the command via `AddCommand<MemoryCommand>("memory")` exactly once; no direct
service registration is permitted in `Program.cs`.

## Inline memory shape in `context` and `trace`

`sharpsense context --include-memories` and `sharpsense trace --include-memories` surface attached memories
inline as `id + tags + stale` (no content) plus a `hint` line for stale memories. To retrieve the full
markdown, use `sharpsense memory get --memory-id <id>` (or the MCP `get_memory` tool).

## Related Pages

* [[cli/mcp-command]] - the MCP `attach_memory` / `delete_memory` / `get_memory` tools that share the same handlers.
* [[persistence/sqlite-schema]] - the cascade FK, the UNIQUE FQDN constraint, and the memory table shape.
