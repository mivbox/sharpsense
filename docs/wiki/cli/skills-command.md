# SharpSense plugin

The `sharpsense` plugin provides code navigation, change assessment, memory maintenance and command-output summaries for Claude Code,
Codex, Copilot CLI, and Copilot in VS Code. It also registers a local stdio MCP connection.

| Skill | Purpose |
| --- | --- |
| `sharpsense-exploring` | Find relevant code, expand unresolved relationships and verify source. |
| `sharpsense-impact-analysis` | Inspect callers, contracts and source evidence before a change. |
| `sharpsense-summarize-output` | Reduce large command output through `ctx_execute`. |
| `sharpsense-memory` | Save, replace, delete or audit code notes when requested. |

Development skills under `.agents/skills/` are repository tooling, separate from the published SharpSense plugin.
They follow the consuming repository's conventions and can move with their reference directories.

| Development skill | Purpose |
| --- | --- |
| `csharp-clean-code` | Write and simplify production C# with clear responsibilities and consistent presentation. |
| `csharp-behavior-tests` | Prove observable behavior with readable cases, meaningful assertions and proportional fixtures. |
| `csharp-feature-work` | Keep application orchestration, ports and transport adapters in the established feature layout. |
| `ef-query-work` | Preserve query shape, persistence semantics and transaction ownership. |
| `async-workflows` | Preserve operation identity, cancellation, bounded work and disposal. |

Skill descriptions identify when each workflow applies. Claude Code and Codex load the matching `SKILL.md` on demand; the
development skills link references only for the relevant work. Examples use portable standards and require no
external reference repository. The four plugin skills include Codex UI metadata and allow implicit selection.
Refresh the client's catalog if updated skills are not visible.

Use graph navigation when relationships are unclear, impact analysis for a proposed contract change, and output
reduction for noisy commands. Use `sharpsense-memory` for remember/forget requests and stale-note reviews;
ordinary exploration and impact analysis retrieve relevant notes themselves. Small commands and existing logs use
ordinary tools. The skills verify workspace binding, avoid unnecessary tracing and preserve source verification.
See [evaluating token usage](../../token-usage.md).

## Prerequisites

Use a client version with plugin support. Install the [SharpSense CLI](../../../README.md), register and analyse a
[workspace](workspace-command.md), then launch the client within that workspace. The CLI must be on the client's
`PATH`; a custom `SHARPSENSE_HOME` must be inherited by the client.

The plugin starts `sharpsense mcp` without an explicit workspace selection. MCP discovers exactly one registered
workspace containing the client project directory, ignoring the saved CLI default. It binds once, until the process restarts.
It does not install the CLI, create or index workspaces, or add hooks.

If no workspace matches, create and analyse one or launch the client from the correct directory. If multiple roots
match, use an explicit [MCP server entry](mcp-command.md) with `--workspace <name-or-id>`. Disable the plugin's automatic
MCP connection in the client when replacing it to avoid duplicate servers. A workspace can span repositories;
its root need not be a Git checkout.

## Install from a local checkout

Use this route to try an unpublished branch. Check out the branch containing the plugin, then replace
`/absolute/path/to/sharpsense` with that checkout's root. Register the root containing the marketplace files,
not the plugin subdirectory.

For Claude Code:

```bash
claude plugin marketplace add /absolute/path/to/sharpsense
claude plugin install sharpsense@sharpsense-marketplace
claude plugin details sharpsense
```

For Codex:

```bash
codex plugin marketplace add /absolute/path/to/sharpsense
codex plugin add sharpsense@sharpsense-marketplace
codex plugin list
```

For Copilot CLI:

```bash
copilot plugin marketplace add /absolute/path/to/sharpsense
copilot plugin install sharpsense@sharpsense-marketplace
copilot plugin list
```

Choose either a local checkout or the GitHub source below for the marketplace named `sharpsense-marketplace`.
Start a new client session after installation and confirm that all four skills are available. Codex marketplace
registration and local plugin loading are described in the [official OpenAI documentation](https://developers.openai.com/plugins/build/plugins);
Copilot commands are documented in the [Copilot CLI plugin reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-plugin-reference).

## Install from GitHub

These commands require the marketplace files and plugin directory to be present on the repository's default branch.
Until that branch contains them, use a local checkout.

For Claude Code:

```bash
claude plugin marketplace add mivbox/sharpsense
claude plugin install sharpsense@sharpsense-marketplace
```

For Codex:

```bash
codex plugin marketplace add mivbox/sharpsense
codex plugin add sharpsense@sharpsense-marketplace
```

To use a published development branch instead, append `--ref <branch>` to the Codex marketplace registration command.

For Copilot CLI:

```bash
copilot plugin marketplace add mivbox/sharpsense
copilot plugin install sharpsense@sharpsense-marketplace
```

## Claude Code skill discovery

Run `claude plugin details sharpsense` to see the four skills and MCP server. Start a new session in the indexed
workspace, or run `/reload-plugins` in an existing session. Skills load when relevant; you can also invoke one
explicitly, for example `/sharpsense:sharpsense-exploring Explain this code path`. Check the server with `/mcp`
and verify the workspace with `graph_stats`.

See the [Claude Code marketplace guide](https://code.claude.com/docs/en/plugin-marketplaces) for installation
and the [plugin manifest reference](https://code.claude.com/docs/en/plugins-reference) for discovery and MCP configuration.

## Copilot in VS Code

For a local checkout, add this to your user `settings.json`, replacing the path with the absolute plugin directory:

```json
{
  "chat.plugins.enabled": true,
  "chat.pluginLocations": {
    "/absolute/path/to/sharpsense/plugins/sharpsense": true
  }
}
```

For the published GitHub marketplace, use:

```json
{
  "chat.plugins.enabled": true,
  "chat.plugins.marketplaces": [
    "mivbox/sharpsense"
  ]
}
```

Then open Extensions, search for `@agentPlugins`, and install `sharpsense` from `sharpsense-marketplace`.
Retain any other entries in your settings when adding these values. A plugin installed through Copilot CLI can also
appear in VS Code's installed plugins view; use one installation source to avoid duplicate skills.

Check the skills through **Chat: Configure Skills**. See the [VS Code plugin guide](https://code.visualstudio.com/docs/agent-customization/agent-plugins)
for marketplace installation, local loading, and client settings.

## Migrate from the embedded installer

The `sharpsense skills` command and embedded skill resources have been removed. Install the plugin through your client;
`sharpsense mcp` starts only the MCP server.

After installing the plugin and confirming skill discovery, review any copies previously exported under
`<install-root>/.agents/skills/`. Back up local edits before manually removing these SharpSense directories:

- `sharpsense-exploring/`
- `sharpsense-impact-analysis/`
- `sharpsense-context-mode/`

Leave unrelated skills in place. This migration does not delete or overwrite user-installed files.

## Skill names

Version 1.1 restores the original exploration and impact-analysis names:

| Previous name | Current name |
| --- | --- |
| `sharpsense-map-code` | `sharpsense-exploring` |
| `sharpsense-assess-change` | `sharpsense-impact-analysis` |
| `sharpsense-context-mode` | `sharpsense-summarize-output` |

Update saved prompts that explicitly invoke the previous names when adopting this version of the plugin.

## Package layout and versions

The canonical skill files live under `plugins/sharpsense/skills/`. All three marketplace catalogs reference the
`plugins/sharpsense/` package:

- `.claude-plugin/marketplace.json` supplies the Claude Code marketplace.
- `.agents/plugins/marketplace.json` supplies the Codex marketplace.
- `.github/plugin/marketplace.json` supplies the Copilot marketplace.

The package uses native client manifests so MCP can start in the client's working directory:

- `plugin.json` references `.mcp.json` for Copilot, with stdio type, `cwd: "."` and tool access.
- `.codex-plugin/plugin.json` declares its MCP server inline, omitting `cwd` so local Codex inherits the session directory.
- `.claude-plugin/plugin.json` declares its MCP server inline with `--workspace-root ${CLAUDE_PROJECT_DIR}`.
- All configurations start the installed `sharpsense` executable with the `mcp` argument.

These client declarations are deliberately separate. Copilot discovers `.mcp.json` before a custom manifest path and otherwise
defaults to the plugin directory. Codex resolves a configured relative `cwd` against the plugin directory, so copying
Copilot's `cwd: "."` into its configuration would prevent workspace discovery. See the
[Codex plugin parser](https://github.com/openai/codex/blob/main/codex-rs/codex-mcp/src/plugin_config.rs) and
[Copilot plugin reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-plugin-reference).
Claude Code loads `.mcp.json` first, then replaces its `sharpsense` entry with the inline declaration. The native
project variable keeps discovery tied to the client project instead of the plugin cache; the server is registered once.
Clients that run plugins in a separate execution environment need an explicit client MCP configuration pointing at
the intended workspace; verify the binding with `graph_stats`.

The root manifest deliberately omits the portable `$schema`: portable plugin MCP configuration resolves its
working directory from the installed plugin root, which would defeat workspace discovery from the project directory.
No executable wrappers or hooks are required.

Plugin version `1.3.0` is independent of the CLI/NuGet version. When releasing a plugin update, keep its identity and
version consistent across all three manifests and marketplace metadata.
