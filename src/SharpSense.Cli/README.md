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
sharpsense search "WorkspaceLoader"
sharpsense context --node-id 42
sharpsense trace 42 -d callee
sharpsense refactor --node-id 42 --new-name Updated
sharpsense skills
```

## Commands

| Command                 | Example                               | Purpose                                                            |
|-------------------------|---------------------------------------|--------------------------------------------------------------------|
| `analyze <target-path>` | `sharpsense analyze SharpSense.sln`   | Build or refresh the local index for a `.sln` or `.csproj` target. |
| `context`               | `sharpsense context --node-id 42`     | Show the immediate callers, callees, and hierarchy breadth for a node. |
| `index <target-path>`   | `sharpsense index SharpSense.sln`     | Legacy alias for `analyze`.                                        |
| `inheritors <node-id>`  | `sharpsense inheritors 232 --toon`    | List direct class inheritors or interface implementers.            |
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

Useful options:

- `--repo-root <path>`: override the repository root used for relative paths and persistence.
- `--no-embeddings`: skip embedding generation during the indexing pass.
- `-v`, `--verbose`: enable verbose logging.

Example:

```bash
sharpsense analyze SharpSense.sln --repo-root /Users/me/src/sharpsense --no-embeddings -v
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

- `analyze <target-path>` loads `sharpsense.yaml` from the directory that contains the target you passed.
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

Notes:

- `includePaths` values are trimmed and empty entries are ignored.
- The globs are used to discover Markdown files that should be indexed alongside C#.
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

The current MCP host exposes five tools:

- `semantic_search`
- `context`
- `trace_node`
- `get_inheritors`
- `refactor_symbol`

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

## Context

Show the immediate architectural breadth for a persisted node:

```bash
sharpsense context --node-id 42
```

The output is compressed TOON with:

- the target node metadata,
- incoming `callers` and `implementers`,
- outgoing `callees` and `inherits`.

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

## Troubleshooting

SharpSense writes command logs under:

```text
~/.SharpSense/logs
```
