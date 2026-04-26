---
title: "Ui Command"
type: cli
tags: [spectre, cqrs, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## Command

`sharp-sense ui` starts the embedded SharpSense web host. It serves bundled UI assets and exposes a dependency-graph API backed by the same query boundary described in [[architecture/cqrs-pipeline]]. Shared bootstrapping follows [[architecture/host-composition]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `Url` | `--url <url>` | HTTP address used by the embedded web host. |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace that owns the persisted graph. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose web-host logging. |

`UiCommand.ConfigureServices()` writes `RepositoryRoot` into `IOptions<SharpSenseCliOptions>`, loads `SharpSenseConfig`, and registers dependency-graph and persistence services before the web app starts.

## Execution Flow

1. `Program.CommandApp.cs` routes `ui` to `UiCommand`.
2. `AbstractWebAsyncCommand<TSettings>` builds a `WebApplication`, configures Serilog request logging, and applies the shared bootstrapping rules from [[architecture/host-composition]].
3. `ConfigureServices()` resolves the repository root and registers `AddDependencyGraph()`, `AddDependencyGraphInfrastructure()`, and `AddPersistence()`.
4. `ConfigureApp()` binds the requested URL and opens the embedded asset namespace.
5. `/api/graph` resolves `IQueryHandler<GetDependencyGraphQuery, GraphResult>` and returns the current dependency graph.
6. Static files and the fallback route serve the embedded UI assets, with a 500 response if the packaged `index.html` asset is missing.
