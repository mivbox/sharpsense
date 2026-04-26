---
title: "Mcp Command"
type: cli
tags: [spectre, mcp, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## Command

`sharp-sense mcp` starts the stdio MCP host for SharpSense tools. It exposes the same indexed read surfaces as [[cli/search-command]] and [[cli/trace-command]], but packages them behind MCP tools instead of CLI output and does not always use the same request defaults. In particular, MCP caller tracing uses the broader `ImpactAnalysisQuery` defaults instead of the CLI trace command's direct-caller shortcut. Shared bootstrapping still follows [[architecture/host-composition]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace used by the MCP tools. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose host logging for the long-lived stdio process. |

`McpCommand.Configure()` writes `RepositoryRoot` into `IOptions<SharpSenseCliOptions>`, loads `SharpSenseConfig`, and composes the read-side feature modules needed by the MCP tool surface.

## Execution Flow

1. `Program.CommandApp.cs` routes `mcp` to `McpCommand`.
2. `AbstractAsyncCommand<TSettings>` builds the host using the shared rules in [[architecture/host-composition]].
3. `Configure()` resolves the repository root and registers repository workspace, `SharpSenseConfig`, hybrid search, embeddings, impact analysis, trace, and persistence.
4. The command adds the MCP server with stdio transport and registers `SharpSenseMcpTools` as the tool surface.
5. Tool serialization adds a `JsonStringEnumConverter<TraceDirection>` so trace directions stay stable across the protocol boundary.
6. `Execute()` does not dispatch a one-shot CQRS payload; it waits for the stdio host to shut down while the registered tools resolve queries on demand.
