# SharpSense 1.0

SharpSense is a local knowledge graph for C#, TypeScript, and Markdown. Named workspaces combine selected
projects, frontend sources, and documentation into one searchable graph. Each workspace owns its configuration,
SQLite database, indexing history, and authored memories.

## Install from source

The CLI requires the .NET 10 SDK. Building from source also requires Node.js 22.13+ and pnpm 12.6.0.
Packaging builds and embeds the UI automatically; running the installed UI does not require Node.js or pnpm.

```bash
git clone https://github.com/mivbox/sharpsense.git
cd sharpsense
dotnet pack src/SharpSense.Cli/SharpSense.Cli.csproj -c Release -p:GeneratePackageOnBuild=false -o artifacts/nuget
dotnet tool install --global --add-source ./artifacts/nuget SharpSense.Cli
sharpsense --version
sharpsense --help
```

Solution NuGet versions are centralized in [Directory.Packages.props](https://github.com/mivbox/sharpsense/blob/main/Directory.Packages.props).
Frontend development and Bash/Kiota client generation are documented in [the UI guide](https://github.com/mivbox/sharpsense/blob/main/src/SharpSense.UI/README.md).
See the [CLI and architecture guide](https://github.com/mivbox/sharpsense/blob/main/docs/wiki/index.md) for further detail.

## Create a workspace

Run `sharpsense workspace create` for guided setup, or provide explicit sources. `sharpsense configure` is an alias:

```bash
sharpsense workspace create commerce --repo-root /path/to/monorepo \
  --csharp src/Api/Api.csproj \
  --csharp src/Core/Core.csproj \
  --csharp src/Jobs/Jobs.csproj \
  --typescript frontend/tsconfig.json \
  --markdown "docs/**/*.md"

sharpsense analyze --workspace commerce
sharpsense ui
```

Open `http://localhost:50069` after starting the UI. It can start before any workspace exists: create a workspace,
discover or add its sources, then analyze it or start watching. Switch between workspaces to explore their graphs,
search symbols, inspect indexing diagnostics, attach memories, and try the analysis tools. The dashboard also supports
editing sources and merging workspaces from the same repository. `ui --workspace commerce` provides an initial selection.

C# sources accept `.sln`, `.slnx`, or `.csproj` files. TypeScript sources accept a configuration file or source directory.
Markdown sources accept a file, directory, or quoted glob. Include documentation explicitly; no repository YAML file
is required or read. For `workspace create`, `add`, and `remove`, relative source paths resolve from the current directory,
or from `--repo-root` when supplied. Absolute paths also work. Stored paths remain relative to the registered repository root.

One workspace can contain all of a product, while another indexes just its API or frontend. Names are convenient
selectors; stable workspace IDs determine storage identity. Renaming or selecting another workspace does not redirect
an existing MCP session to a different graph.

## Manage workspaces

```bash
sharpsense workspace                         # Show command help
sharpsense workspace ls                      # Alias for list; marks the default
sharpsense workspace use commerce            # Save the global CLI default
sharpsense analyze                           # Analyze that default workspace
sharpsense workspace show commerce --json
sharpsense workspace rename old-name commerce
sharpsense workspace add commerce --repo-root /path/to/monorepo --markdown "design/**/*.md"
sharpsense workspace remove commerce --repo-root /path/to/monorepo --markdown "design/**/*.md"

sharpsense workspace create api --repo-root /path/to/monorepo --csharp src/Api/Api.csproj
sharpsense workspace create web --repo-root /path/to/monorepo --typescript frontend/tsconfig.json
sharpsense workspace merge product api web
```

Workspace management uses explicit subcommands. Creation offers bounded source discovery, manual paths/globs, and a review before saving. Fully specified creation commands
save without prompting; incomplete commands require an interactive terminal. JSON mode always requires explicit inputs.
Running `sharpsense` or `sharpsense workspace` without a subcommand shows help.

`remove` reports each removed or unmatched source, including when zero sources matched. Its JSON output adds
`removedSources` and `unmatchedSources` to the workspace details. Unmatched requests succeed without rewriting the
definition. Removal does not delete the workspace or its database. `merge` creates
a new workspace from the source sets of existing workspaces in the same repository, preserving the originals.
Stop an active analyzer or watcher before changing sources. Reindex after changing sources, and restart an MCP process
to bind the updated definition. A workspace writer lock prevents concurrent CLI/UI indexing and source edits.
Removing sources from the next index also removes symbols and memories attached to those symbols.

Configuration and data live outside the repository:

```text
~/.sharpsense/workspaces/<workspace-id>/
  workspace.yaml
  index.db
```

Set `SHARPSENSE_HOME` to an absolute directory to use another storage location. Configuration schema version 1 contains
`version`, `id`, `name`, `repositoryRoot`, and explicit `sources`.

`workspace use <name-or-id>` saves one global default in `~/.sharpsense/default-workspace` (or under `SHARPSENSE_HOME`).
It stores the workspace ID, so renaming preserves the selection. Multiple workspaces can share a repository.
CLI analysis and query commands use explicit `--workspace` first, then the saved default, then a single workspace
matching the current repository. `--repo-root` only changes that final repository lookup.
Interactive `analyze` offers a picker or setup when no default is saved and repository selection is ambiguous or missing;
redirected commands never prompt. Empty or whitespace workspace selectors are rejected; omit `--workspace` to use the default.
Invalid explicit or saved selections remain errors. `workspace ls` stays available if the saved default is invalid,
with a warning on stderr and valid JSON on stdout when `--json` is used.

MCP always requires `--workspace <name-or-id>` and keeps that workspace for its lifetime. Changing the CLI default
does not affect running MCP servers or watchers. The global UI provides its own workspace switcher; use `ui --workspace`
to choose its initial workspace.

## Analyze and query

```bash
sharpsense analyze --workspace commerce --watch
sharpsense analyze --workspace commerce --no-embeddings
sharpsense search "payment handler" --workspace commerce
sharpsense context --node-id 42 --workspace commerce
sharpsense trace 42 --direction caller --workspace commerce
sharpsense doctor --workspace commerce --json
sharpsense mcp --workspace commerce
```

Interactive analysis shows elapsed time, phase, per-language source activity, embeddings, saving, and a committed
summary. Watch mode distinguishes reconciliation, idle, updates, recovery, and stopping. Redirected output uses
concise milestones. Unknown totals stay indeterminate rather than presenting an overall percentage.

An analysis builds the union of the configured sources and commits it together. Failed extraction preserves the previous
graph. Watch mode first indexes the workspace, then processes changes; failed incremental work gets one full recovery
attempt before reporting failure.

C# project loading does not promote warnings to errors for analysis. MSBuild and NuGet warnings remain visible in index
diagnostics, including package vulnerability advisories. The repository's build settings are unchanged. Genuine project
loading failures still stop indexing and preserve the previous graph.

C# edits to existing source files reuse the loaded Roslyn workspace and compilation caches while extracting the complete
semantic graph. Structural, project, `.props`/`.targets`, and additional-input changes use conservative reloads.
TypeScript updates refresh the configured TypeScript scope to reconcile imports, deleted files, and configuration changes.
Embedding reuse avoids regenerating unchanged vectors. Index status reports phase timings and generated/reused counts.

## Connect an MCP client

Configure and analyze a workspace first, then register SharpSense as a stdio MCP server. For clients that use an
`mcpServers` configuration, the entry looks like this:

```json
{
  "mcpServers": {
    "sharpsense-commerce": {
      "command": "sharpsense",
      "args": ["mcp", "--workspace", "commerce"]
    }
  }
}
```

Replace `commerce` with your workspace name or ID. The client must be able to find the installed `sharpsense` command
and use the same `SHARPSENSE_HOME` if you configured a custom home. Each MCP process stays bound to its selected workspace;
register separate server entries to expose multiple workspaces. The global UI selection does not change an MCP session.

Tools include semantic search, symbol context, dependency tracing, impact analysis, memories, and `graph_stats`.

## Install agent skills

The `sharpsense` plugin provides the existing exploration, impact-analysis, and context-mode skills for Codex,
Copilot CLI, and Copilot in VS Code. Install it separately after configuring the CLI, workspace, and MCP connection.
The plugin contains skills and metadata only; it does not install or configure the MCP server.

See the [plugin installation guide](https://github.com/mivbox/sharpsense/blob/main/docs/wiki/cli/skills-command.md)
for local-checkout and GitHub installation, VS Code settings, and migration from the removed `sharpsense skills` command.
Skill names and instructions are unchanged.

## Index diagnostics

`doctor` checks the selected home-owned configuration, SDK discovery, native TypeScript/TSX parsers, embedding assets,
database compatibility, and graph counts. It does not create, migrate, reset, or index a database. Errors return exit code 1;
warnings alone return 0. Asset checks verify model availability rather than executing inference.

The UI and read-only `graph_stats` MCP tool report node, edge, file, project, language, embedding, and memory counts,
plus the last successful index and latest completed attempt. Failed or cancelled attempts preserve the previous success.
Diagnostics are bounded and indicate truncation. These timestamps describe recorded indexing work, not filesystem freshness.

## Language support

C# analysis uses Roslyn semantics. TypeScript/TSX analysis uses packaged native Tree-sitter grammars to extract exported
declarations, members, components, module imports, package dependencies, and statically recognizable HTTP requests.
The graph distinguishes same-named declarations in different files and tracks source hashes for stale memories.

TypeScript analysis is syntax-based. Import edges represent module dependencies, not compiler-resolved function calls.
It does not provide TypeScript type checking or complete resolution of dynamic imports, computed URLs, runtime dispatch,
namespace merging, arbitrary framework wrappers, or package export conditions.

Memories belong to persisted numeric node IDs within one workspace database. Surviving node updates preserve authored
notes; rebuilding an empty database creates a new set of IDs and does not recreate those notes.

## Version 1 breaking changes

Version 1 requires registered workspaces. `analyze <target>`, the old `index` alias, and project-local `sharpsense.yaml`
configuration are removed. Configure the desired sources, then run `analyze --workspace <name>`.
The `refactor` command and `refactor_symbol` MCP tool are also removed.

Older repository-hash databases under `~/.SharpSense` are left untouched and are not automatically adopted. Keep backups
and use a compatible older version to retrieve authored memories before retiring an old index. New workspace databases
start independently; do not copy an incompatible database containing legacy values such as `DocumentHierarchy` into them.

To configure this repository from source, explicitly include its previous documentation selection:

```bash
dotnet run --project src/SharpSense.Cli -- workspace create sharpsense --repo-root "$PWD" \
  --csharp SharpSense.sln \
  --typescript src/SharpSense.UI/tsconfig.json \
  --markdown "docs/**/*.md"
dotnet run --project src/SharpSense.Cli -- analyze --workspace sharpsense
```
