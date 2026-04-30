---
title: "Mcp Command"
type: cli
tags: [spectre, mcp, implemented]
created: 2026-04-26
updated: 2026-05-01
confidence: high
---

## Command

`sharp-sense mcp` starts the stdio MCP host for SharpSense tools. It exposes the same indexed read surfaces as [[cli/search-command]], [[cli/trace-command]], and [[cli/inheritors-command]], and packages those reads behind MCP tools instead of CLI output. MCP caller tracing still uses the broader `ImpactAnalysisQuery` defaults instead of the CLI trace command's direct-caller shortcut. Shared bootstrapping still follows [[architecture/host-composition]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace used by the MCP tools. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose host logging for the long-lived stdio process. |

`McpCommand.Configure()` writes `RepositoryRoot` into `IOptions<SharpSenseCliOptions>`, loads `SharpSenseConfig`, and composes the read-side feature modules needed by the MCP tool surface.

## Tool Surface

| Tool | Backing query | Purpose |
| --- | --- | --- |
| `semantic_search` | `HybridSearchQuery` | Hybrid BM25 + vector search over indexed code nodes. |
| `trace_node` | `TraceQuery` / `ImpactAnalysisQuery` | Downstream callees or upstream caller blast radius for a known node id. |
| `get_inheritors` | `GetInheritorsQuery` | Direct derived classes or interface implementers for a persisted node id. |

## Execution Flow

1. `Program.CommandApp.cs` routes `mcp` to `McpCommand`.
2. `AbstractAsyncCommand<TSettings>` builds the host using the shared rules in [[architecture/host-composition]].
3. `Configure()` resolves the repository root and registers repository workspace, `SharpSenseConfig`, hybrid search, embeddings, inheritors, impact analysis, trace, and persistence.
4. The command adds the MCP server with stdio transport and registers `SharpSenseMcpTools` as the tool surface.
5. Tool serialization adds a `JsonStringEnumConverter<TraceDirection>` so trace directions stay stable across the protocol boundary.
6. `get_inheritors` resolves `GetInheritorsQuery` through the `IInheritorFinder` read slice and formats direct class inheritors or interface implementers with the shared TOON output formatter.
7. `Execute()` does not dispatch a one-shot CQRS payload; it waits for the stdio host to shut down while the registered tools resolve queries on demand.
