# SharpSense documentation

SharpSense indexes selected C#, TypeScript, and Markdown sources into a local graph. Use the CLI, MCP tools, or browser UI to search symbols and documents, inspect relationships, and attach persistent memories.

Start with the [project README](../../README.md) for installation and prerequisites, then [create a workspace](cli/workspace-command.md). A workspace names one repository checkout and the sources to index; its configuration and database live under `~/.sharpsense`, or an absolute `SHARPSENSE_HOME` override.

## Workflows

| Guide | Use it to |
| --- | --- |
| [Configure and manage workspaces](cli/workspace-command.md) | Select projects, a frontend, and documentation; create independent merged selections. |
| [Analyze and watch](cli/analyze-command.md) | Build the graph and keep the selected sources indexed. |
| [Doctor and graph statistics](cli/doctor-command.md) | Inspect setup, index coverage, and recorded indexing failures. |
| [Browser UI](cli/ui-command.md) | Manage workspaces, run indexing jobs, explore the graph, and try tools. |
| [Search](cli/search-command.md) | Find relevant code and documentation. |
| [Context](cli/context-command.md) | Inspect a node's immediate relationships. |
| [Trace](cli/trace-command.md) | Follow dependencies and callers. |
| [Inheritors](cli/inheritors-command.md) | Find direct derived classes and implementers. |
| [Memories](cli/memory-command.md) | Attach and retrieve persistent notes about indexed nodes. |
| [MCP](cli/mcp-command.md) | Expose one workspace to an MCP client over stdio. |
| [Command execution](cli/execute-command.md) | Return bounded excerpts from local command output. |
| [Agent skills](cli/skills-command.md) | Install the bundled exploration, impact-analysis, and context-mode guides. |

## Architecture

| Area | Documentation |
| --- | --- |
| Application boundaries | [Vertical slices](architecture/vertical-slice-application.md), [commands and queries](architecture/cqrs-pipeline.md), [host composition](architecture/host-composition.md) |
| Indexing | [Source discovery](architecture/file-discovery.md), [watch reconciliation](architecture/incremental-watch.md), [filesystem boundaries](architecture/virtual-file-system.md) |
| Languages | [C# and Roslyn](extractors/csharp.md), [TypeScript and Tree-sitter](extractors/typescript.md), [Markdown and Markdig](extractors/markdown.md) |
| Queries and storage | [SQLite schema](persistence/sqlite-schema.md), [hybrid search](architecture/hybrid-search-pipeline.md), [workspace explorer](architecture/workspace-tree.md) |
| Output reduction | [Windowed command execution](architecture/windowed-execution-pipeline.md), [evaluating token usage](../token-usage.md) |

Version 1 uses named workspaces throughout. Project-local `sharpsense.yaml`, positional analyze targets, the `index` alias, and refactoring commands/tools are no longer supported. The [documentation history](log.md) records earlier designs and is not a guide to current behavior.
