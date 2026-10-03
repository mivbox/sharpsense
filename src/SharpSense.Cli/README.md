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

Solution NuGet versions are centralised in [Directory.Packages.props](https://github.com/mivbox/sharpsense/blob/main/Directory.Packages.props).
Frontend development and Bash/Kiota client generation are documented in [the UI guide](https://github.com/mivbox/sharpsense/blob/main/src/SharpSense.UI/README.md).

## Create a workspace

Run `sharpsense workspace create` for guided setup, or provide explicit sources. `sharpsense configure` is an alias:

```bash
sharpsense workspace create commerce --workspace-root /path/to/monorepo \
  --csharp src/Api/Api.csproj \
  --csharp src/Core/Core.csproj \
  --csharp src/Jobs/Jobs.csproj \
  --typescript frontend/tsconfig.json \
  --markdown "docs/**/*.md"

sharpsense analyze --workspace commerce
sharpsense ui
```

Open `http://localhost:50069` after starting the UI. It can start before any workspace exists: create a workspace,
discover or add its sources, then analyse it or start watching. Switch between workspaces to explore their graphs,
search symbols, inspect indexing diagnostics, attach memories, and try the analysis tools. The dashboard also supports
editing sources and merging workspaces with the same root. `ui --workspace commerce` provides an initial selection.

C# sources accept `.sln`, `.slnx`, or `.csproj` files. TypeScript sources accept a configuration file or source directory.
Markdown sources accept a file, directory, or quoted glob. Include documentation explicitly; no repository YAML file
is required or read. For `workspace create`, `add`, and `remove`, relative source paths resolve from the current directory,
or from `--workspace-root` when supplied. Absolute paths also work. Stored paths remain relative to the registered workspace root. The root stays at the directory you choose;
it does not expand to a containing Git repository. Use a common parent to include sources from several repositories.
`--repo-root` remains an alias for `--workspace-root`.

One workspace can contain all of a product, while another indexes just its API or frontend. Names are convenient
selectors; stable workspace IDs determine storage identity. Renaming or selecting another workspace does not redirect
an existing MCP session to a different graph.

## Manage workspaces

```bash
sharpsense workspace                         # Show command help
sharpsense workspace ls                      # Alias for list; marks the default
sharpsense workspace use commerce            # Save the global CLI default
sharpsense analyze                           # Analyse that default workspace
sharpsense workspace show commerce --json
sharpsense workspace rename old-name commerce
sharpsense workspace add commerce --workspace-root /path/to/monorepo --markdown "design/**/*.md"
sharpsense workspace remove commerce --workspace-root /path/to/monorepo --markdown "design/**/*.md"

sharpsense workspace create api --workspace-root /path/to/monorepo --csharp src/Api/Api.csproj
sharpsense workspace create web --workspace-root /path/to/monorepo --typescript frontend/tsconfig.json
sharpsense workspace merge product api web
```

Workspace management uses explicit subcommands. Creation offers bounded source discovery, manual paths/globs, and a review before saving. Fully specified creation commands
save without prompting; incomplete commands require an interactive terminal. JSON mode always requires explicit inputs.
Running `sharpsense` or `sharpsense workspace` without a subcommand shows help.

`remove` reports each removed or unmatched source, including when zero sources matched. Its JSON output adds
`removedSources` and `unmatchedSources` to the workspace details. Unmatched requests succeed without rewriting the
definition. Removal does not delete the workspace or its database. `merge` creates
a new workspace from the source sets of existing workspaces with the same root, preserving the originals.
Stop an active analyser or watcher before changing sources. Reindex after changing sources, and restart an MCP process
to bind the updated definition. A workspace writer lock prevents concurrent CLI/UI indexing and source edits.
Removing sources from the next index also removes symbols and memories attached to those symbols.

Configuration and data live outside the repository:

```text
~/.sharpsense/workspaces/<workspace-id>/
  workspace.yaml
  index.db
```

Set `SHARPSENSE_HOME` to an absolute directory to use another storage location. Configuration schema version 1 contains
`version`, `id`, `name`, `workspaceRoot`, and explicit `sources`. Existing definitions using `repositoryRoot`
remain readable; saves use `workspaceRoot`. CLI JSON and HTTP responses retain their `repositoryRoot` field.

`workspace use <name-or-id>` saves one global default in `~/.sharpsense/default-workspace` (or under `SHARPSENSE_HOME`).
It stores the workspace ID, so renaming preserves the selection. Multiple workspaces can share a repository.
CLI analysis and query commands use explicit `--workspace` first, then the saved default. They do not infer a
workspace from the current directory. Interactive `analyze` offers a picker or setup when no default is saved;
redirected commands never prompt. Empty or whitespace workspace selectors are rejected. Invalid explicit or saved
selections remain errors. `workspace ls` stays available if the saved default is invalid, with a warning on stderr
and valid JSON on stdout when `--json` is used.

MCP uses an explicit `--workspace` or, when omitted, the single registered workspace whose root contains its launch
directory. It never uses the CLI default. Zero or multiple matches require an explicit selection, including when
roots overlap. Each MCP process keeps its workspace until restarted. The global UI provides its own workspace
switcher; use `ui --workspace` to choose its initial workspace.

## Analyse and query

```bash
sharpsense analyze --workspace commerce --watch
sharpsense analyze --workspace commerce --no-embeddings
sharpsense search "payment handler" --workspace commerce
sharpsense context --node-id 42 --workspace commerce
sharpsense trace 42 --direction caller --workspace commerce
sharpsense doctor --workspace commerce --json
sharpsense mcp --workspace commerce
```

Query commands such as `search`, `context` and `trace` return JSON by default. Add `--toon` for compact TOON output.
Search accepts plain text, including symbol names and paths containing `*` or `:`. Keyword search combines prefix
terms with OR; FTS operators, column selectors and quoted phrases have no special meaning. Tokens shorter than two
characters are ignored, and punctuation-only input returns no hits.

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

## Install the agent plugin

The `sharpsense` plugin includes exploration, impact-analysis, memory-maintenance and command-output skills, plus a stdio MCP connection
for Codex and Copilot. Install the SharpSense CLI on the client's `PATH`, then create and analyse a workspace before
starting the client from a directory within that workspace.

For Copilot CLI:

```bash
copilot plugin marketplace add mivbox/sharpsense
copilot plugin install sharpsense@sharpsense-marketplace
```

For Codex:

```bash
codex plugin marketplace add mivbox/sharpsense
codex plugin add sharpsense@sharpsense-marketplace
```

To try an unpublished checkout, replace `mivbox/sharpsense` in the marketplace command with its absolute path.
Start a new client session after installation. The plugin starts `sharpsense mcp` and discovers a unique workspace
from the client's launch directory. It does not install the CLI or create or index a workspace. If you use a custom
`SHARPSENSE_HOME`, launch the client with the same absolute environment value.

For Copilot in VS Code, enable plugins and add the marketplace to your user settings:

```json
{
  "chat.plugins.enabled": true,
  "chat.plugins.marketplaces": ["mivbox/sharpsense"]
}
```

Retain other marketplace entries. Open Extensions, search for `@agentPlugins`, and install `sharpsense` from
`sharpsense-marketplace`. Check the available skills through **Chat: Configure Skills**.

## MCP startup and workspace selection

Workspace selection happens once when the MCP process starts:

1. If `--workspace <name-or-id>` is supplied, use that workspace regardless of the current directory.
2. Otherwise, find registered workspaces whose root contains the process's current directory, including the root itself.
3. Start only when exactly one workspace matches. With no matches or multiple matches, report an error and require
   an explicit selection. Overlapping roots are ambiguous; the nearest root does not take priority.

**MCP never reads the default saved by `sharpsense workspace use`.** That default is for CLI commands such as
`analyze`, `search` and `context`. An invalid default cannot prevent MCP directory discovery, and a valid default
cannot resolve an ambiguous directory.

```bash
cd /path/to/commerce/backend
sharpsense mcp                         # Discover the unique containing workspace
sharpsense mcp --workspace commerce    # Select explicitly, from any directory
sharpsense mcp --workspace-root /path/to/commerce/backend  # Set the discovery directory
```

`--repo-root` remains an alias for `--workspace-root`. The discovery directory can be inside a nested Git repository;
matching uses the configured workspace roots. For example, roots `/work/product` and `/work/product/frontend` both
match a launch from `/work/product/frontend`, so use `--workspace` to choose between them.

The selected workspace stays fixed for the process's lifetime. Restart MCP to select another workspace or pick up
changed workspace configuration. Changing the CLI default or UI selection does not switch a running MCP server.
The plugin's automatic connection uses this same startup behaviour.

## Connect an MCP client directly

If several workspaces match the launch directory, select one explicitly. Disable the plugin's automatic MCP
connection in your client when replacing it with a manual entry to avoid a duplicate server. For clients using
an `mcpServers` configuration:

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

Replace `commerce` with your workspace name or ID. The same entry also works without the plugin. Omit the
`--workspace` argument and value to use directory discovery. Each MCP process stays bound to its selected workspace;
register separate entries to expose several workspaces. Changing `workspace use` or the UI selection does not
change an MCP session.

Tools include semantic search, symbol context, dependency tracing, impact analysis, memories and `graph_stats`.

## Index diagnostics

`doctor` checks the selected home-owned configuration, SDK discovery, native TypeScript/TSX parsers, embedding assets,
database compatibility, and graph counts. It does not create, migrate, reset, or index a database. Errors return exit code 1;
warnings alone return 0. Asset checks verify model availability rather than executing inference.

The UI and read-only `graph_stats` MCP tool report node, edge, file, project, language, embedding, and memory counts,
plus the last successful index and latest completed attempt. Failed or cancelled attempts preserve the previous success.
Diagnostics are bounded and indicate truncation. These timestamps describe recorded indexing work, not filesystem freshness.

## Language support

C# analysis uses Roslyn semantics. TypeScript/TSX analysis uses packaged native Tree-sitter grammars to extract exported
declarations, members, components, module imports, package dependencies, and statically recognisable HTTP requests.
The graph distinguishes same-named declarations in different files and tracks source hashes for stale memories.

TypeScript analysis is syntax-based. Import edges represent module dependencies, not compiler-resolved function calls.
It does not provide TypeScript type checking or complete resolution of dynamic imports, computed URLs, runtime dispatch,
namespace merging, arbitrary framework wrappers, or package export conditions.

Memories belong to persisted numeric node IDs within one workspace database. Surviving node updates preserve authored
notes; rebuilding an empty database creates a new set of IDs and does not recreate those notes.

## Development

The C# projects share .NET 10 build, analyser and test settings. Follow [AGENTS.md](https://github.com/mivbox/sharpsense/blob/main/AGENTS.md) and [.editorconfig](https://github.com/mivbox/sharpsense/blob/main/.editorconfig).
Use internal implementations, public cross-assembly contracts and feature-based handlers. Tests cover observable
behaviour with xUnit, Moq and AwesomeAssertions.

To configure this repository from source:

```bash
dotnet run --project src/SharpSense.Cli -- workspace create sharpsense --workspace-root "$PWD" \
  --csharp SharpSense.sln \
  --typescript src/SharpSense.UI/tsconfig.json \
  --markdown "docs/**/*.md"
dotnet run --project src/SharpSense.Cli -- analyze --workspace sharpsense
```

Validate changes with:

```bash
dotnet restore SharpSense.sln
dotnet format SharpSense.sln --no-restore --severity warn --verify-no-changes \
  --exclude src/SharpSense.Infrastructure/Persistence/Migrations
dotnet build SharpSense.sln --no-restore -c Release -t:Rebuild -p:GeneratePackageOnBuild=false
dotnet test SharpSense.sln --no-build -c Release
```

After building the frontend once, backend-only builds can reuse its assets with `-p:FrontendBuildCompleted=true`.

## License

SharpSense is available under the [MIT license](https://github.com/mivbox/sharpsense/blob/main/LICENSE).
