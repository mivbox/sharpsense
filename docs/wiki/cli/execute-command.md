---
title: "Execute Command"
type: cli
tags: [spectre, sqlite, implemented]
created: 2026-05-08
updated: 2026-05-10
confidence: high
---

## Command

`sharp-sense execute <command>` runs a quoted local command string inside the resolved repository root, captures its streamed output, and returns either structured JSON or compact TOON excerpts. The route shares the execution/reduction boundary documented in [[architecture/windowed-execution-pipeline]] and the host bootstrap rules in [[architecture/host-composition]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `Command` | positional `<command>` | Raw command string to execute without a shell. Quote the full value when it contains spaces. |
| `Query` | `-q\|--query <QUERY>` | Optional FTS query used to locate relevant log lines before windowing and interval merging. |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the working directory used for the command invocation. |
| `UseToonFormat` | `--toon` | Emits metadata-first TOON output instead of JSON. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose host logging. |

## Execution Flow

1. `Program.CommandApp.cs` routes `execute` to `ExecuteCommand`.
2. Spectre binds the quoted `<command>` string and optional `--query`, validates that the command is not blank, and passes shared verbosity settings through [[architecture/host-composition]].
3. `Configure()` resolves `RepositoryRoot` through `CommandPathResolver`, stores it in `IOptions<SharpSenseCliOptions>`, loads `sharpsense.yaml`, and registers `AddCommandExecutionInfrastructure()`.
4. `Execute()` resolves `ICommandProcessRunner` plus `IExecuteLogIndexFactory`, then forwards the `CommandExecutionRequest` into the shared CLI `CommandExecutionReducer`.
5. `CommandExecutionReducer` launches the command through `ICommandProcessRunner`, asks `IExecuteLogIndexFactory` for a per-invocation `TransientExecutionLogDbContext`-backed FTS5 index, and reduces the transcript through merged context windows as described in [[architecture/windowed-execution-pipeline]].
6. When `--query` is missing or produces no hits, the result contains only compact metadata and summary text. Otherwise the route returns merged line blocks with explicit `Lstart-end` ranges.
7. `--toon` formats the result through `TokenObjectNotation.SerializeCommandExecutionResult()`; the default path serializes the same model as JSON.
8. The command exit code mirrors the executed process exit code on success, or returns `1` when SharpSense itself cannot start or reduce the command.

## Implementation Notes

- `IExecuteLogIndexFactory` creates a dedicated `TransientExecutionLogDbContext` through `IDbContextFactory<TransientExecutionLogDbContext>`.
- That DbContext opens a private SQLite `:memory:` connection and bootstraps the FTS5 table per command invocation.
- The execution transcript is never written to the repository database or to disk.
- `MATCH` is used to locate relevant lines, but final excerpts are returned in capture order so the reduced output stays readable as a log.
