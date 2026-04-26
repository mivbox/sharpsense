---
title: "Analyze Command"
type: cli
tags: [spectre, cqrs, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## Command

`sharp-sense analyze <target-path>` is the primary Spectre.Console route for indexing a Target. `sharp-sense index <target-path>` is a legacy alias that resolves to the same `AnalyzeCommand` class, so both routes share the same DI composition, option binding, indexing flow, and watch behavior. This command is the CLI entrypoint for the [[architecture/cqrs-pipeline]] boundary, and its shared bootstrapping rules live in [[architecture/host-composition]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `TargetPath` | positional `<target-path>` | Identifies the Target to index. |
| `RepositoryRoot` | `--repo-root <path>` | Overrides the repository root used to resolve relative paths. |
| `Watch` | `--watch` | Keeps the process alive and applies incremental updates after the initial index. |
| `SkipEmbeddings` | `--no-embeddings` | Disables embedding generation during the full index pass. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose logging for the command host. It does not flow through `SharpSenseCliOptions`. |

`AnalyzeCommand.Configure()` resolves the repository root with `CommandPathResolver`, then copies `TargetPath`, `RepositoryRoot`, `Watch`, and `SkipEmbeddings` into `IOptions<SharpSenseCliOptions>`. The same method also calls `AddSharpSenseConfiguration(targetDirectory)` so the Target-local `sharpsense.yaml` file can populate `IOptions<SharpSenseConfig>` and publish change tokens for include-path updates. CLI values become host configuration, while runtime payloads stay in [[architecture/cqrs-pipeline]] command records.

## Execution Flow

1. `Program.cs` builds the command app, and `Program.CommandApp.cs` routes both `analyze` and `index` to `AnalyzeCommand`.
2. Spectre binds CLI arguments into `AnalyzeCommand.Settings` and rejects empty Targets in `Validate()`.
3. `AbstractAsyncCommand<TSettings>` creates a host, configures Serilog, and calls `AnalyzeCommand.Configure()` to register repository workspace, Target-local `SharpSenseConfig`, application indexing, embeddings, indexing infrastructure, and persistence services.
4. `Execute()` starts the full index by resolving `ICommandHandler<IndexTargetCommand>` from a scope and sending a payload that contains only progress reporters.
5. `KnowledgeGraphIndexing` reads `IOptions<SharpSenseCliOptions>` to resolve the configured Target and repository root, then runs extraction, optional embeddings, and persistence.
6. When `Watch` is disabled, the command prints the indexed Target and exits.
7. When `Watch` is enabled, `GetWatchPath()` reads the configured repository root from `IOptions<SharpSenseCliOptions>`.
8. `WatchWorkspace()` resolves `IWorkspaceWatcher`, starts the watch loop, and forwards each debounced batch to `ICommandHandler<UpdateWorkspaceFilesCommand>`.
9. If an incremental batch fails inside `ApplyBatchUpdate()`, the command falls back to a full reindex and then continues inside the current watcher session.
10. If `IWorkspaceWatcher.Watch()` fails at the watcher level, the command falls back to a full reindex, prints a restart message, and then re-enters watch mode from the outer retry loop.
