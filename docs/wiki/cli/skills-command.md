# SharpSense skills plugin

The `sharpsense` plugin packages the existing exploration, impact-analysis, and context-mode skills for Codex,
Copilot CLI, and Copilot in VS Code. Skill names, frontmatter, and instructions are unchanged.

| Skill | Purpose |
| --- | --- |
| `sharpsense-exploring` | Search, inspect context, and navigate the graph. |
| `sharpsense-impact-analysis` | Inspect callers and dependencies before a change. |
| `sharpsense-context-mode` | Reduce large command output through `ctx_execute`. |

## Prerequisites

Use a client version with plugin support. Install the [SharpSense CLI](../../../README.md), register and analyze a
[workspace](workspace-command.md), then configure a [workspace-bound MCP server](mcp-command.md) in your client.

The plugin contains skills and metadata only. It does not install the CLI, create or index workspaces, configure MCP
servers, or add hooks. The skills require the corresponding SharpSense tools to be available in the client.

## Install from a local checkout

Use this route to try an unpublished branch. Check out the branch containing the plugin, then replace
`/absolute/path/to/sharpsense` with that checkout's root. Register the root containing the marketplace files,
not the plugin subdirectory.

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
Start a new client session after installation and confirm that all three skills are available. Codex marketplace
registration and local plugin loading are described in the [official OpenAI documentation](https://developers.openai.com/plugins/build/plugins);
Copilot commands are documented in the [Copilot CLI plugin reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-plugin-reference).

## Install from GitHub

These commands require the marketplace files and plugin directory to be present on the repository's default branch.
Until that branch contains them, use a local checkout.

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

## Package layout and versions

The canonical skill files live under `plugins/sharpsense/skills/`. Both marketplace catalogs reference the
`plugins/sharpsense/` package:

- `.agents/plugins/marketplace.json` supplies the Codex marketplace.
- `.github/plugin/marketplace.json` supplies the Copilot marketplace.

The root `plugins/sharpsense/plugin.json` is the portable manifest, and
`plugins/sharpsense/.codex-plugin/plugin.json` supplies Codex compatibility metadata.
Plugin version `1.0.0` is independent of the CLI/NuGet version. When releasing a plugin update, keep its identity and
version consistent across both manifests and marketplace metadata.
