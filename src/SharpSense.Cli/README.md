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
sharpsense trace 42 -d callee
sharpsense skills
```

## Commands

| Command                 | Example                               | Purpose                                                            |
|-------------------------|---------------------------------------|--------------------------------------------------------------------|
| `analyze <target-path>` | `sharpsense analyze SharpSense.sln`   | Build or refresh the local index for a `.sln` or `.csproj` target. |
| `index <target-path>`   | `sharpsense index SharpSense.sln`     | Legacy alias for `analyze`.                                        |
| `inheritors <node-id>`  | `sharpsense inheritors 232 --toon`    | List direct class inheritors or interface implementers.            |
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

## Skills

Install the embedded SharpSense skills into `.agents/skills` under the current working directory:

```bash
sharpsense skills
```

Install into a custom root instead:

```bash
sharpsense skills /tmp/sharpsense-agent-workspace
```

The current CLI package explicitly embeds `sharpsense/SKILL.md`, and the command writes that skill under:

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

## Inheritors

List direct derived classes for a class node or direct implementing classes for an interface node:

```bash
sharpsense inheritors 232 --toon
```

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
