# Configure and manage workspaces

A workspace is a named selection of sources from one repository checkout. Multiple workspaces can select different parts of the same monorepo; each has its own graph and memories.

## Create a workspace

Run `sharpsense configure` in an interactive terminal, or provide the full selection:

```bash
sharpsense configure product --repo-root /path/to/repository \
  --csharp backend/Api.csproj \
  --csharp shared/Contracts.csproj \
  --typescript frontend/tsconfig.json \
  --markdown 'docs/**/*.md'
```

Replace the example paths with existing sources. The language flags are repeatable. C# accepts a project or solution; TypeScript accepts a tsconfig file or directory; Markdown accepts a file, directory, or glob. Quote globs so the shell does not expand them.

`sharpsense workspace create product ...` creates the same definition without interactive prompts. Creation registers sources; run [analyze](analyze-command.md) separately to build the index.

## Select a workspace

Use `--workspace <name-or-id>` on analyze, doctor, query, memory, execution, and MCP commands. Without it, SharpSense selects the single registered workspace matching the current repository. If none or more than one matches, the command asks you to configure or select one explicitly.

`--repo-root <path>` changes the repository lookup or source-path base; it does not create an implicit workspace. Source paths are stored relative to the canonical repository root.

```bash
sharpsense workspace list
sharpsense workspace show product --json
sharpsense analyze --workspace product
sharpsense search "request validation" --workspace product --toon
```

Numeric node IDs belong to their workspace. Keep the same workspace selected when passing a search result to context, trace, or memory commands.

## Change or combine sources

```bash
sharpsense workspace add product --markdown 'runbooks/**/*.md'
sharpsense workspace remove product --markdown 'runbooks/**/*.md'
sharpsense workspace merge combined backend frontend
sharpsense analyze --workspace combined --watch
```

Merge requires at least two workspaces from the same repository. It creates a new, independent definition with deduplicated sources; it does not copy an index or memories, and later edits to the originals do not change the merged workspace.

Stop active analysis/watch before changing a workspace's sources, then analyze again. A cross-process workspace lock prevents another CLI or UI process from indexing or changing that workspace concurrently. Other workspaces can continue operating independently.

## Storage

Configuration and data live under `~/.sharpsense`, or an absolute `SHARPSENSE_HOME` override:

```text
.sharpsense/
  logs/
  workspaces/
    <workspace-guid>/
      workspace.yaml
      index.db
      .index.lock
```

The database appears when initialized. The lock file may remain after shutdown; its open OS handle controls exclusivity, not the file's presence. Do not delete it to interrupt a running indexer.

A definition has this shape:

```yaml
version: 1
id: 01234567-89ab-4cde-8123-456789abcdef
name: product
repositoryRoot: /path/to/repository
sources:
  - kind: cSharp
    path: backend/Api.csproj
  - kind: typeScript
    path: frontend/tsconfig.json
  - kind: markdown
    path: docs/**/*.md
```

Use configure, workspace commands, or the [UI](ui-command.md) to manage definitions. IDs remain stable when a workspace is renamed or its sources change. Definitions are saved atomically. SharpSense does not read or generate project-local `sharpsense.yaml`.

Version 1 does not automatically adopt or delete legacy databases. Register a workspace and analyze its sources to create a fresh index; preserve any old data you still need.
