---
title: "Inheritors Command"
type: cli
tags: [spectre, cqrs, implemented]
created: 2026-05-01
updated: 2026-05-01
confidence: high
---

## Command

`sharp-sense inheritors <node-id>` lists direct class inheritors for a persisted class node and direct implementing classes for a persisted interface node. It is the CLI counterpart to `get_inheritors` on [[cli/mcp-command]], and its shared bootstrapping still follows [[architecture/host-composition]] and the read-side rules in [[architecture/cqrs-pipeline]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `NodeId` | positional `<node-id>` | Persisted integer code-node id whose direct inheritors or implementers should be listed. |
| `UseToonFormat` | `--toon` | Emits TOON-formatted inheritor rows. |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace that owns the SQLite index. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose command-host logging. |

`InheritorsCommand.Configure()` copies `RepositoryRoot` into `IOptions<SharpSenseCliOptions>`, loads `SharpSenseConfig`, and registers the inheritors and persistence feature slices.

## Execution Flow

1. `Program.CommandApp.cs` routes `inheritors` to `InheritorsCommand`.
2. Spectre binds the integer node id and rejects non-positive values during validation.
3. `AbstractAsyncCommand<TSettings>` builds the host using the shared rules in [[architecture/host-composition]].
4. `Configure()` resolves the repository root and registers `AddInheritors()`, `AddInheritorsInfrastructure()`, and `AddPersistence()`.
5. `Execute()` resolves `IQueryHandler<GetInheritorsQuery, CodeNodeResult[]>` and dispatches the node id as a read-side query.
6. `InheritorFinder` reads persisted `DependencyEdges` where `CalleeNodeId` matches the requested node and `EdgeType` is `Implements`.
7. The finder interprets those stored edges as either class inheritance or interface implementation depending on the target node and projects the caller rows through the shared navigation query.
8. Output is written as JSON by default or as TOON when `--toon` is enabled.
