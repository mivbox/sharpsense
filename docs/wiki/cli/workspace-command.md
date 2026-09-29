# Configure and manage workspaces

A workspace is a named selection of sources beneath a configured root. It can span repositories under a common parent, or select part of a monorepo. Each workspace has its own graph and memories. The root is normalised as supplied; it never expands to a Git root.

Run `sharpsense workspace` to show subcommand help. Use `ls`, `use`, `create`, `show`, `rename`, `add`, `remove`, and `merge` directly.

## Create a workspace

Run `sharpsense workspace create` in an interactive terminal, or provide the full selection:

```bash
sharpsense workspace create product --workspace-root /path/to/repository \
  --csharp backend/Api.csproj \
  --csharp shared/Contracts.csproj \
  --typescript frontend/tsconfig.json \
  --markdown 'docs/**/*.md'
```

Replace the example paths with existing sources. The language flags are repeatable. C# accepts a project or solution; TypeScript accepts a tsconfig file or directory; Markdown accepts a file, directory, or glob. Quote globs so the shell does not expand them.

`sharpsense configure` is an alias for `workspace create`, with the same arguments and prompting rules. Fully specified commands save without prompting. Missing names or sources are prompted for in interactive terminals, with a review before saving. Redirected input and `--json` require a name and explicit sources and never prompt.

Interactive creation offers bounded source discovery or manual paths/globs, followed by a review before saving. Discovery skips dependency and build directories. Creation registers sources; run [analyze](analyze-command.md) separately to build the index.

## Select a workspace

Save a global default with `sharpsense workspace use <name-or-id>`. `workspace ls` (an alias for `list`) marks it as `(default)`; JSON entries include `isDefault`. This is one default for all directories, not a per-repository mapping. Multiple workspaces may still share the same repository. If the default file cannot be read, listing still succeeds and writes a warning to stderr; JSON stdout remains a valid array. Use a listed name or ID with `workspace use` to replace the default.

Analyze, doctor, query, memory, execution, and `workspace show` select in this order:

1. Explicit `--workspace <name-or-id>` (or the positional selector for `workspace show`).
2. The saved global default.

CLI commands do not discover a workspace from the current directory. When no default is saved, interactive `analyze` offers a picker or setup. Other commands and redirected execution report the required selection. Invalid explicit or saved selections remain errors; run `workspace use` again to replace an unavailable default, or override it with `--workspace`. Empty or whitespace selectors are rejected, including an empty shell variable passed to `--workspace`; omit the option to use the default.

MCP uses an explicit `--workspace` or discovers the single workspace whose root contains its launch directory. It never reads the saved CLI default. No matches or multiple matches, including overlapping roots, require explicit selection. Each MCP server retains its selected workspace for its lifetime, so separate instances can serve different workspaces. Changing the default does not rebind running servers or watchers. The global UI has its own workspace switcher and accepts `ui --workspace` for an initial selection.

`--workspace-root <path>` sets the source-path base for creation and source edits, or the discovery directory for MCP. `--repo-root` remains an alias. It does not select a CLI workspace. `workspace show` accepts either a positional name/ID or `--workspace`, not both. Catalog mutations use positional names or IDs and do not accept `--workspace`. Listing, selecting the default, renaming, and merging do not accept `--workspace-root`.

For `workspace create`, `add`, and `remove`, relative source paths resolve from the process's actual working directory, or from `--workspace-root` when supplied. Relative `--workspace-root` paths use that same directory; an inherited `PWD` value does not override it. Absolute source paths also work. This applies even when running from a nested directory. `add` and `remove` use the same source-path base rules as creation. Use `--workspace-root /path/to/repository` to pass paths copied from `workspace show`.

The source-path base does not relocate an existing workspace. All sources must remain beneath its root; stored paths remain relative to that normalised root. Guided source selection uses the workspace directory displayed by its prompt. Choose a common parent root to include sibling repositories.

```bash
sharpsense workspace ls
sharpsense workspace use product
sharpsense workspace show --json
sharpsense analyze
sharpsense analyze --workspace backend
sharpsense search "request validation" --workspace product --toon
```

Numeric node IDs belong to their workspace. Keep the same workspace selected when passing a search result to context, trace, or memory commands.

## Change or combine sources

```bash
sharpsense workspace rename old-name product
sharpsense workspace add product --workspace-root /path/to/repository --markdown 'runbooks/**/*.md'
sharpsense workspace remove product --workspace-root /path/to/repository --markdown 'runbooks/**/*.md'
sharpsense workspace merge combined backend frontend
sharpsense analyze --workspace combined --watch
```

Removal matches registered source kind/path pairs; a Markdown glob identifies the configured source rather than expanding into individual files. Text output lists removed and unmatched sources, including a count of zero when nothing matched. JSON output preserves the workspace fields and adds `removedSources` and `unmatchedSources` arrays. Mixed requests remove matching sources and report unmatched ones; repeated removals succeed with zero matches. A request with no matches does not rewrite the workspace definition.

Merge requires at least two workspaces with the same normalised workspace root. It creates a new, independent definition with deduplicated sources; it does not copy an index or memories, and later edits to the originals do not change the merged workspace.

Stop active analysis/watch before changing a workspace's sources, then analyze again. A cross-process workspace lock prevents another CLI or UI process from indexing or changing that workspace concurrently. Other workspaces can continue operating independently.

## Storage

Configuration and data live under `~/.sharpsense`, or an absolute `SHARPSENSE_HOME` override:

```text
.sharpsense/
  default-workspace
  logs/
  workspaces/
    <workspace-guid>/
      workspace.yaml
      index.db
      .index.lock
```

`default-workspace` stores only the selected workspace UUID and is written atomically by `workspace use`. It survives renames and does not modify repository files or workspace definitions. Each `SHARPSENSE_HOME` has its own default.

The database appears when initialized. The lock file may remain after shutdown; its open OS handle controls exclusivity, not the file's presence. Do not delete it to interrupt a running indexer.

A definition has this shape:

```yaml
version: 1
id: 01234567-89ab-4cde-8123-456789abcdef
name: product
workspaceRoot: /path/to/workspace
sources:
  - kind: cSharp
    path: backend/Api.csproj
  - kind: typeScript
    path: frontend/tsconfig.json
  - kind: markdown
    path: docs/**/*.md
```

Existing version 1 definitions using `repositoryRoot` remain readable without rewriting them. Saves write only
`workspaceRoot`; IDs, database paths and memories are preserved. If both keys appear, they must name the same
normalised absolute directory. CLI JSON and HTTP payloads retain `repositoryRoot` for compatibility.

Use configure, workspace commands, or the [UI](ui-command.md) to manage definitions. IDs remain stable when a workspace is renamed or its sources change. Definitions are saved atomically. SharpSense does not read or generate project-local `sharpsense.yaml`.

Version 1 does not automatically adopt or delete legacy databases. Register a workspace and analyze its sources to create a fresh index; preserve any old data you still need.
