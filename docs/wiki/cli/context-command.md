---
title: "Context Command"
type: cli
tags: [spectre, mcp, implemented]
created: 2026-05-02
updated: 2026-05-11
confidence: high
---

## Command

`sharp-sense context --node-id <node-id>` returns the immediate architectural breadth around a persisted node: target metadata plus incoming callers/implementers and outgoing callees/inherits. It is the CLI counterpart to MCP `context` on [[cli/mcp-command]], and both routes now dispatch the same `GetNodeContextQuery` read slice described in [[architecture/cqrs-pipeline]] and bootstrapped through [[architecture/host-composition]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `NodeId` | `--node-id <NODE_ID>` | Persisted integer code-node id whose immediate breadth should be loaded. |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace that owns the SQLite index. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose command-host logging. |

`ContextCommand.Configure()` copies `RepositoryRoot` into `IOptions<SharpSenseCliOptions>`, loads `SharpSenseConfig`, and registers the Context360 Application slice, Context360 Infrastructure slice, and persistence.

## Execution Flow

1. `Program.CommandApp.cs` routes `context` to `ContextCommand`.
2. Spectre binds `--node-id` and rejects non-positive values during validation.
3. `AbstractAsyncCommand<TSettings>` builds the host using the shared rules in [[architecture/host-composition]].
4. `Configure()` resolves the repository root and registers `AddContext360()`, `AddContext360Infrastructure()`, and `AddPersistence()`.
5. `Execute()` resolves `IQueryHandler<GetNodeContextQuery, Context360Result>` and dispatches a query containing the persisted node id plus the fixed related-node cap.
6. `GetNodeContextQueryHandler` validates the node id, clamps `MaxRelated`, and delegates to `IContextRepository`.
7. `ContextRepository` uses one DbContext to load the target node plus immediate incoming callers/implementers and outgoing callees/inherits, projecting directly into `Context360Result` and stripping related-method parameter lists inside the EF projection.
8. `TokenObjectNotation.SerializeContext360()` renders the YAML-like TOON block written directly to the terminal.
