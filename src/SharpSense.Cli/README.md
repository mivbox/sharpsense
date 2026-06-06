# SharpSense CLI

SharpSense builds a local knowledge graph for C# and Markdown, then lets you query it from the terminal, an MCP client, or the embedded UI.

The NuGet package installs the `sharpsense` command.

## Install

```bash
dotnet tool install --global SharpSense.Cli
sharpsense --help
```

## Quick start

```bash
sharpsense analyze path/to/YourSolution.sln
sharpsense search "WorkspaceLoader" --include-memories
sharpsense context --node-id 42 --include-memories
sharpsense execute "dotnet test SharpSense.sln" --query "error OR failed"
sharpsense trace 42 -d callee --include-memories
sharpsense refactor --node-id 42 --new-name Updated
sharpsense memory add --node-id 42 --content "Always greet politely." --tag convention
sharpsense memory get --memory-id 5d3f7e2c-7c1a-4f4b-9e2c-2a6f1c2e8d1b
sharpsense skills
```

## Commands

| Command                 | Example                               | Purpose                                                            |
|-------------------------|---------------------------------------|--------------------------------------------------------------------|
| `analyze <target-path>` | `sharpsense analyze SharpSense.sln`   | Build or refresh the local index for a `.sln`, `.csproj`, or directory target backed by `sharpsense.yaml`. |
| `context`               | `sharpsense context --node-id 42`     | Show the immediate callers, callees, and hierarchy breadth for a node. |
| `execute <command>`     | `sharpsense execute "dotnet test SharpSense.sln" --query "error OR failed"` | Run a local command and reduce its output into compact JSON or TOON excerpts. |
| `index <target-path>`   | `sharpsense index SharpSense.sln`     | Legacy alias for `analyze`.                                        |
| `inheritors <node-id>`  | `sharpsense inheritors 232 --toon`    | List direct class inheritors or interface implementers.            |
| `memory <action>`       | `sharpsense memory add --node-id 42 --content "..."` | Attach, list, or remove persistent semantic memories on a code node. |
| `refactor`              | `sharpsense refactor --node-id 42 --new-name Updated` | Semantically rename an indexed symbol through Roslyn. |
| `search <query>`        | `sharpsense search "WorkspaceLoader"` | Search the persisted index for matching code nodes.                |
| `trace <identifier>`    | `sharpsense trace 42 -d caller`       | Trace callers or callees for an indexed node.                      |
| `skills [install-root]` | `sharpsense skills /repo`             | Write the embedded SharpSense skills into `.agents/skills`.        |
| `mcp`                   | `sharpsense mcp --repo-root /repo`    | Start the SharpSense MCP server over stdio.                        |
| `ui`                    | `sharpsense ui --repo-root /repo`     | Start the embedded web UI and dependency graph API.                |

## Analyse and index a target

Use `analyze` for the initial index build.

```bash
sharpsense analyze SharpSense.sln
```

You can also index a single project:

```bash
sharpsense analyze src/SharpSense.Cli/SharpSense.Cli.csproj
```

You can also index a directory when that directory contains `sharpsense.yaml`:

```bash
sharpsense analyze docs
```

Directory targets are configuration-driven:

- SharpSense reads `docs/sharpsense.yaml`.
- `includePaths` controls which Markdown files under that directory are indexed.
- Directory targets do **not** infer a C# solution or project. Use a `.sln` or `.csproj` target when you want Roslyn-backed C# indexing.

Useful options:

- `--repo-root <path>`: override the repository root used for relative paths and persistence.
- `--no-cache`: ignore persisted embedding reuse and force fresh embedding generation for analyzed nodes.
- `--no-embeddings`: skip embedding generation during the indexing pass.
- `-v`, `--verbose`: enable verbose logging.

Example:

```bash
sharpsense analyze SharpSense.sln --repo-root /Users/me/src/sharpsense --no-cache -v
```

## Watch mode

Use watch mode when you want SharpSense to keep the index warm after the first pass.

```bash
sharpsense analyze SharpSense.sln --watch
```

Watch mode:

- runs a full index first,
- stays alive,
- applies incremental updates for changed C# and Markdown files,
- keeps using the same local repository index.

This is the command to leave running while you edit code locally.

## Execute

Run a quoted local command string inside the resolved repository root and return either structured JSON or compact TOON excerpts:

```bash
sharpsense execute "dotnet test SharpSense.sln" --query "error OR failed"
```

Useful options:

- `-q`, `--query <QUERY>`: apply an FTS query to find relevant output lines before SharpSense merges surrounding context windows.
- `--repo-root <path>`: override the repository root used as the working directory for the command.
- `--toon`: emit compact TOON output instead of JSON.
- `-v`, `--verbose`: enable verbose logging.

Notes:

- Quote the full `<command>` value when it contains spaces.
- The command is executed without a shell.
- When `--query` is omitted or finds no hits, SharpSense returns only compact command metadata and summary text.
- On success, the CLI exits with the executed process exit code. If SharpSense cannot start or reduce the command, it exits with `1`.

## Refactor command input model

`refactor` takes the persisted node id plus the new identifier name:

```bash
sharpsense refactor --node-id 42 --new-name Updated
```

Notes:

- The CLI contract mirrors MCP `refactor_symbol(nodeId, newName)`.
- `newName` must be the identifier only, not a signature or code block.
- The persisted node already resolves the target document path and owning project path from the SQLite index.
- C# nodes use Roslyn semantic rename across the loaded workspace. Markdown nodes use a targeted local heading rename inside the persisted Markdown span.
- `--target <path>` is only an override for source-backed renames when you want to force a different `.sln` or `.csproj`.

## `sharpsense.yaml`

SharpSense reads an optional `sharpsense.yaml` file for Markdown discovery configuration.

Today the supported setting is:

```yaml
includePaths:
  - docs/**/*.md
  - README.md
```

How it is resolved:

- `analyze <target-path>` loads `sharpsense.yaml` from the target directory. For file targets, that means the directory that contains the file. For directory targets, that means the directory itself.
- Read-side commands such as `search`, `trace`, `inheritors`, `context`, `mcp`, and `ui` load `sharpsense.yaml` from the resolved repository root (`--repo-root` or the current working directory).

Examples:

```text
repo/
├── SharpSense.sln
├── sharpsense.yaml
└── docs/
```

If you index a solution in the repo root:

```bash
sharpsense analyze SharpSense.sln
```

place `sharpsense.yaml` next to `SharpSense.sln`.

If you index a single project:

```bash
sharpsense analyze src/SharpSense.Cli/SharpSense.Cli.csproj
```

place `sharpsense.yaml` in `src/SharpSense.Cli/`.

If you index a directory directly:

```bash
sharpsense analyze docs
```

place `sharpsense.yaml` in `docs/`.
Use globs relative to `docs/`, for example:

```yaml
includePaths:
  - "**/*.md"
```

Notes:

- `includePaths` values are trimmed and empty entries are ignored.
- The globs are evaluated relative to the target directory that owns `sharpsense.yaml`.
- Solution and project targets use those globs to index Markdown alongside C#.
- A directory target is only useful when that directory has a `sharpsense.yaml` file.
- Directory targets currently drive Markdown discovery only.
- After changing `sharpsense.yaml`, rerun a full `analyze` so the persisted index reflects the new include set.

## Skills

Install the embedded SharpSense skills into `.agents/skills` under the current working directory:

```bash
sharpsense skills
```

Install into a custom root instead:

```bash
sharpsense skills /tmp/sharpsense-agent-workspace
```

The current CLI package embeds the exported SharpSense skill pack:

- `sharpsense-refactoring/SKILL.md`
- `sharpsense-exploring/SKILL.md`
- `sharpsense-impact-analysis/SKILL.md`

The command writes those skills under:

```text
<install-root>/.agents/skills
```

## MCP is separate from indexing

`mcp` does **not** build the index. It exposes SharpSense tools over stdio for an MCP client to call.

Typical workflow:

1. Build the index once with `analyze`.
2. Optionally keep it fresh with `analyze --watch` in a separate terminal.
3. Start the MCP host.

```bash
sharpsense analyze SharpSense.sln
sharpsense mcp --repo-root /Users/me/src/sharpsense
```

If you want live updates while an MCP client is connected, run `analyze --watch` and `mcp` as separate processes.

## MCP tool outputs

The current MCP host exposes nine tools:

- `semantic_search`
- `context`
- `trace_node`
- `get_inheritors`
- `refactor_symbol`
- `attach_memory` (with `intent` argument)
- `delete_memory`
- `get_memory` (single id)
- `get_memories` (batch ids)

The examples below use real fixture outputs and rough token estimates based on output length (`~characters / 4`), so expect some tokenizer/model variance.

### `semantic_search`

Use this when you need a compact shortlist of likely matching nodes grouped by directory and file.

Example output (`~60` tokens for this sample):

```text
src/SharpSense.Infrastructure/DependencyGraph/:
  DependencyGraphMapper.cs:
    - [M] `553` ToExternalGraphNode L20-21
    - [M] `556` ToGraphNode L32-47

src/SharpSense.Domain/KnowledgeGraph/Nodes/:
  ProjectNode.cs:
    - [P] `373` Id L5
```

### `context`

Use this when you already know the node id and want an immediate callers/callees/implements/inherits snapshot.

Example output (`~80` tokens for this sample):

```text
node:
  id: 42
  name: PaymentProcessor.ProcessPayment(string, int)
  kind: M
  file: src/Fixture.App/PaymentProcessor.cs:12-30

incoming:
  callers: [HttpEndpoint.Handle (Id:7)]
  implementers: [PaymentProcessorBase (Id:8)]

outgoing:
  callees: [ReceiptWriter.WriteReceipt (Id:9)]
  inherits: [IPaymentProcessor (Id:10)]
```

### `trace_node`

Use this when you want a caller or callee chain for a known node id.

Example output (`~40` tokens for this sample):

```text
- [M] `1` MessageConsumer.Render @ src/Fixture.App/MessageConsumer.cs:L20-28
  -> [M] `3` MessageProvider.GetMessage @ src/Fixture.App/MessageProvider.cs:L7-11
```

### `get_inheritors`

Use this when you need direct derived classes or interface implementers for a known node id.

Example output (`~30` tokens for this sample):

```text
[C] `7` DerivedAlpha @ src/Fixture.App/DerivedAlpha.cs:3-16
[C] `8` DerivedBeta @ src/Fixture.App/DerivedBeta.cs:3-17
```

### `refactor_symbol`

Use this when you already know the persisted node id and want to semantically rename that declared symbol.

Example output (`~20` tokens for this sample):

```text
refactor_success: true
modified_files:
  - src/Fixture.App/PaymentProcessor.cs
```

### `attach_memory`

Use this when the agent has confirmed a non-obvious behaviour, invariant, or convention on a code node and wants it
to persist across sessions. The payload is markdown and survives a full workspace re-parse; the persisted memory
includes an `IsStale` flag that flips to `true` if the target method's `BodyHash` changes.

### `delete_memory`

Use this when a previously attached memory is no longer correct (the user retracted the intent, the behaviour was
rewritten, or the user explicitly asked to forget it). Memories are immutable once attached, so the only edit
verbs are `attach_memory` and `delete_memory`.

### `get_memory`

Use this to fetch the full markdown content of a memory by its persistent `Guid`. `context` and `trace_node` only
surface memory **id + intent + tags + stale** inline (to keep context small); call `get_memory` to retrieve the
content when you actually need to act on a memory. Stale memories (`IsStale: true`) render an inline `hint`
reminding the agent to call `delete_memory + attach_memory` to refresh.

### `get_memories`

Batch fetch by id. Pass `memoryIds: Guid[]`; the tool returns one rendered memory block per id, all in a
single round-trip. Use this after a multi-step trace surfaced a list of memory ids and you want their content.

## Context

Show the immediate architectural breadth for a persisted node:

```bash
sharpsense context --node-id 42
```

Add `--include-memories` to surface attached memories inline as `id + tags + stale` (no content; use
`sharpsense memory get` to fetch the full markdown):

```bash
sharpsense context --node-id 42 --include-memories
```

The output is compressed TOON with:

- the target node metadata,
- incoming `callers` and `implementers`,
- outgoing `callees` and `inherits`,
- structural `parents` and `children` (with a `memories: N (X stale; ...)` hint when `--include-memories` is set),
- an optional `semantic_context:` block with the inline memory shape described in the [Memory](#memory)
  section.

## Inheritors

List direct derived classes for a class node or direct implementing classes for an interface node:

```bash
sharpsense inheritors 232 --toon
```

## Refactor

Semantically rename an indexed symbol:

```bash
sharpsense refactor --node-id 352 --new-name RenderMessage
```

Options:

- `--node-id <NODE_ID>`
- `--new-name <NEW_NAME>`: the new identifier only.
- `--target <path>`: optional explicit `.sln` or `.csproj` target.
- `--repo-root <path>`

Notes:

- `refactor` behaves like Rider `Ctrl+R, R` or Visual Studio `F2` for indexed symbols.
- If `--target` is omitted, SharpSense prefers repo-root workspace discovery and falls back to the persisted owning project when needed.
- Keep `sharpsense analyze --watch` running if you want the graph and vector index to refresh automatically after the write.

## Memory

Manage persistent semantic memories through the CLI. The same handlers back the MCP `attach_memory`,
`delete_memory`, `get_memory`, and `get_memories` tools, so the CLI and the MCP surface stay in lock-step.

Attach a memory (with an intent classification that the agent can filter on at retrieval time):

```bash
sharpsense memory add --node-id 42 --content "Always greet politely." --tag convention --intent Invariant
```

List every memory attached to a node (summary only — use `get` to fetch full content). Filter by intent:

```bash
sharpsense memory list --node-id 42
sharpsense memory list --node-id 42 --intent-filter Invariant --intent-filter Warning
```

Fetch the full content of a single memory by its persistent Guid:

```bash
sharpsense memory get --memory-id 5d3f7e2c-7c1a-4f4b-9e2c-2a6f1c2e8d1b
```

Batch fetch multiple memories in one round-trip (for a multi-step trace that just surfaced a list of ids):

```bash
sharpsense memory get --memory-ids 5d3f7e2c-7c1a-4f4b-9e2c-2a6f1c2e8d1b,8f0a2c41-ddee-4e6f-9d2a-b9c81ad4b5d2
```

Remove a memory by its persistent Guid:

```bash
sharpsense memory remove --memory-id 5d3f7e2c-7c1a-4f4b-9e2c-2a6f1c2e8d1b
```

Options (shared):

- `--repo-root <path>`: override the repository root used for the persisted index.
- `-v`, `--verbose`: enable verbose logging.

### Memory intents

Memories are classified with an intent enum (`Convention` | `Invariant` | `Todo` | `Warning` | `Decision`).
The default is `Convention`. The agent can filter retrievals by intent — e.g. pull all `Invariant` memories
across a node, or all `Warning` memories across a trace — instead of digging through every memory by id.
Intents also make the inline shape filterable: a `Warning` memory stays visible inline until the agent
deletes it, while a `Todo` memory naturally phases out as work completes.

### Inline memory shape in `context` and `trace`

`sharpsense context --include-memories` and `sharpsense trace --include-memories` surface attached memories
inline as `id + tags + stale` only — the full content is **not** rendered. This keeps the inline context
small. When a memory is stale, the inline shape appends a `hint: "call delete_memory + attach_memory to refresh"`.
To retrieve the full content, call `sharpsense memory get --memory-id <id>` (or the MCP `get_memory` tool).

Example (one stale memory on the target node):

```text
structural:
  parents: []
  children: []
  memories: 1 (1 stale; use get_memory <id> to fetch content)

semantic_context:
  memories:
    - [M] id=5d3f7e2c-7c1a-4f4b-9e2c-2a6f1c2e8d1b stale=true tags=[convention, security] hint="call delete_memory + attach_memory to refresh"
```

Notes:

- The CLI/MCP boundary uses the persisted integer `NodeId`; the FQDN is resolved internally and never crosses the
  tool boundary.
- Memories are immutable once attached. The only write verbs are `add` (attach) and `remove` (delete).
- The `IsStale` flag on a memory flips to `true` automatically when the target method's `BodyHash` changes during
  a re-parse. The agent should then `remove` the stale memory and re-`add` the corrected one.
- Cascade: when a code node is removed from the index (e.g. the underlying file is deleted and the workspace is
  re-parsed), the SQLite cascade foreign key on `MemoryNodeRecord.TargetFullyQualifiedName` automatically removes
  the matching memories.

## Search

Search the existing index:

```bash
sharpsense search "workspace loader"
```

Use TOON format for automation-friendly output:

```bash
sharpsense search "workspace loader" --toon
```

## Trace

Trace relationships from an indexed node id, canonical id, or fully qualified name:

```bash
sharpsense trace 42 -d callee
sharpsense trace 42 -d caller
sharpsense trace "SharpSense.Infrastructure.CodeAnalysis.Roslyn.WorkspaceLoader.UpdateDocuments(string, System.Collections.Generic.IReadOnlyList<SharpSense.Application.Indexing.Models.WorkspaceFileChange>, System.Threading.CancellationToken)" -d caller
```

Options:

- `-d`, `--direction <caller|callee>`
- `--toon`
- `--include-memories` — surface attached memories inline as `id + tags + stale` (no content; use
  `sharpsense memory get --memory-id <id>` to fetch the full markdown).
- `--repo-root <path>`

## UI

Start the embedded UI:

```bash
sharpsense ui --repo-root /Users/me/src/sharpsense
```

Bind to a custom address:

```bash
sharpsense ui --repo-root /Users/me/src/sharpsense --url http://127.0.0.1:8080
```

The UI exposes a right-hand **Memory panel** alongside the graph viewport. Type a node id (or click a node
in the graph) to focus the panel, then list, view, add, and delete memories for that node. The panel
surfaces inline metadata only (id + intent + tags + stale flag) and lazily fetches full content per id on
demand, mirroring the CLI/MCP token-economy.

The UI's HTTP surface is documented for tooling authors:

| Verb | Path | Purpose |
| --- | --- | --- |
| `GET`  | `/api/memory/node/{nodeId}` | List inline metadata for one node. `?intents=Invariant,Warning` filters by intent. |
| `GET`  | `/api/memory/{memoryId}` | Fetch the full content of a single memory. |
| `GET`  | `/api/memory?ids=g1,g2,…` | Batch fetch multiple memories in one round-trip. |
| `POST` | `/api/memory/node/{nodeId}` | Add a memory. JSON body: `{ "content": "...", "tags": [...], "intent": "Invariant" }`. |
| `DELETE` | `/api/memory/{memoryId}` | Remove a memory by id. |

## Troubleshooting

SharpSense writes command logs under:

```text
~/.SharpSense/logs
```
