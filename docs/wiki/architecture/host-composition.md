---
title: "Host Composition"
type: architecture
tags: [spectre, cqrs, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## The Problem

Every command needs the same lifecycle guarantees: resolve a Target consistently, bootstrap logging, bind configuration once, and compose feature modules without turning `Program.cs` into a registration dump.

## The Approach

SharpSense centralizes route bootstrapping in two base classes. `AbstractAsyncCommand<TSettings>` builds a generic host for console commands, while `AbstractWebAsyncCommand<TSettings>` builds a `WebApplication` for long-lived HTTP routes such as [[cli/ui-command]]. Each concrete command inherits `GlobalSettings`, uses `CommandPathResolver` to normalize repository and Target paths, copies CLI values into `IOptions<SharpSenseCliOptions>`, and loads `sharpsense.yaml` through `AddSharpSenseConfiguration()`. `AnalyzeCommand` points that configuration loader at the Target directory, while the read-side commands point it at the repository root. Feature modules are then chained through layer-specific `Add*` extension methods, keeping command routing thin and consistent with [[architecture/cqrs-pipeline]].

## Components Involved

| Component | Role |
| --- | --- |
| `Program.CommandApp.cs` | Registers the `analyze`, `index`, `search`, `trace`, `mcp`, and `ui` routes. |
| `AbstractAsyncCommand<TSettings>` | Builds the generic host, configures Serilog, starts the host, and delegates execution to the command. |
| `AbstractWebAsyncCommand<TSettings>` | Builds the web host, wires request logging, and delegates HTTP app configuration. |
| `GlobalSettings` | Adds the shared `-v\|--verbose` switch for every command. |
| `CommandPathResolver` | Resolves repository roots from `PWD` or `CurrentDirectory` and normalizes Target directories. |
| `SharpSenseCliOptions` | Holds host-scoped CLI configuration such as `RepositoryRoot`, `TargetPath`, `Watch`, and `SkipEmbeddings`. |
| `SharpSenseConfig` | Holds `sharpsense.yaml` include-path settings loaded from the directory passed to `AddSharpSenseConfiguration()`; `AnalyzeCommand` uses the Target directory, while the read-side commands use the repository root. |
| `*ServiceCollectionExtensions` | Register application handlers and infrastructure implementations per feature slice. |

## Strict Rules

1. Bootstrap command hosts through `AbstractAsyncCommand<TSettings>` or `AbstractWebAsyncCommand<TSettings>` instead of building hosts directly in `Program.cs`.
2. Resolve repository roots and Target directories with `CommandPathResolver`; do not duplicate path-joining logic inside commands.
3. Copy CLI settings into `IOptions<SharpSenseCliOptions>` during `Configure()` or `ConfigureServices()` and keep runtime payloads in CQRS records as described in [[architecture/cqrs-pipeline]].
4. Load `sharpsense.yaml` through `AddSharpSenseConfiguration()` so include-path filters and change tokens stay consistent across commands.
5. Compose features through modular `Add*` extension methods rather than direct one-off registrations in `Program.cs`.
