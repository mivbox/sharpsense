# Agent skills

Install the bundled skill guides in the current directory or an explicit install root:

```bash
sharpsense skills
sharpsense skills /path/to/repository
```

Files are written under `.agents/skills/`. This command does not require a registered workspace.

| Skill | Purpose |
| --- | --- |
| `sharpsense-exploring` | Search, inspect context, and navigate the graph. |
| `sharpsense-impact-analysis` | Inspect callers and dependencies before a change. |
| `sharpsense-context-mode` | Reduce large command output through `ctx_execute`. |

The guides depend on the corresponding SharpSense MCP tools being available to the agent. Connect a [workspace-bound MCP server](mcp-command.md) and use your agent client's skill-loading conventions.

`sharpsense mcp --workspace product --skills` also exports skills during startup. Refactoring skills are no longer included.
