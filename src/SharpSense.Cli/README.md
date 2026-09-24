# SharpSense 1.0

SharpSense is a local knowledge graph for C#, TypeScript, and Markdown. Named workspaces combine selected
projects, frontend sources, and documentation into one searchable graph. Each workspace owns its configuration,
SQLite database, indexing history, and authored memories.

## Install from source

The CLI requires the .NET 10 SDK. Building from source also requires Node.js 22.13+ and pnpm 11.8.0.
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

Run `sharpsense configure` for guided setup, or provide explicit sources:

```bash
sharpsense configure commerce --repo-root /path/to/monorepo \
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
is required or read. Source paths are stored relative to the registered repository root.

One workspace can contain all of a product, while another indexes just its API or frontend. Names are convenient
selectors; stable workspace IDs determine storage identity. Renaming or selecting another workspace does not redirect
an existing MCP session to a different graph.

## Manage workspaces

```bash
sharpsense workspace                         # Guided terminal manager
sharpsense workspace list
sharpsense workspace show commerce --json
sharpsense workspace rename old-name commerce
sharpsense workspace add commerce --markdown "design/**/*.md"
sharpsense workspace remove commerce --markdown "design/**/*.md"

sharpsense workspace create api --repo-root /path/to/monorepo --csharp src/Api/Api.csproj
sharpsense workspace create web --repo-root /path/to/monorepo --typescript frontend/tsconfig.json
sharpsense workspace merge product api web
```

The guided manager selects, creates, inspects, renames, edits, and merges workspaces, and can start analysis or watch.
Setup offers bounded source discovery, manual paths/globs, and a review before saving. Explicit commands and JSON
output remain suitable for scripts. Running `sharpsense` without a command still shows help.

`remove` removes selected sources from a definition; it does not delete the workspace or its database. `merge` creates
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
`version`, `id`, `name`, `repositoryRoot`, and explicit `sources`. Commands accept `--workspace <name-or-id>`.
Without that option, graph commands resolve a single registered workspace for the current repository. When several
workspaces match, select one explicitly. Interactive `analyze` also offers a workspace picker or setup; redirected
commands never prompt, and an invalid explicit selection remains an error. `--repo-root` is a lookup hint and never implicitly creates a workspace.

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
dotnet run --project src/SharpSense.Cli -- configure sharpsense --repo-root "$PWD" \
  --csharp SharpSense.sln \
  --typescript src/SharpSense.UI/tsconfig.json \
  --markdown "docs/**/*.md"
dotnet run --project src/SharpSense.Cli -- analyze --workspace sharpsense
```
