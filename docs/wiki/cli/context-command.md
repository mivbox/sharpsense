---
title: "Context Command"
type: cli
tags: [spectre, mcp, implemented]
created: 2026-05-02
updated: 2026-05-02
confidence: high
---

## Command

`sharp-sense context --node-id <node-id>` returns the immediate architectural breadth around a persisted node: target metadata plus incoming callers/implementers and outgoing callees/inherits. It is the CLI counterpart to MCP `context` on [[cli/mcp-command]], and both routes share the same injected `IContextService` boundary described in [[architecture/cqrs-pipeline]] and bootstrapped through [[architecture/host-composition]].

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
5. `Execute()` resolves `IContextService` directly instead of a query handler because both CLI and MCP must share the exact same breadth assembly logic.
6. `ContextLookup` reads the target node plus immediate incoming callers/implementers and outgoing callees/inherits from persisted `DependencyEdges`.
7. `ContextService` strips method parameters from related method names and returns a compact `Context360Result`.
8. `TokenObjectNotation.SerializeContext360()` renders the YAML-like TOON block written directly to the terminal.
