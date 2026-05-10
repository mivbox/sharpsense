---
title: "Windowed Execution Pipeline"
type: architecture
tags: [sqlite, mcp, cqrs, implemented]
created: 2026-05-08
updated: 2026-05-10
confidence: high
---

## The Problem

AI agents need to run real local commands such as builds and audits, but raw terminal telemetry is too large and too noisy to forward directly into model context. If the host returns the full transcript, the caller spends tokens on unfiltered progress chatter instead of the small slices that explain success or failure.

## The Approach

SharpSense now routes command execution through a shared CLI reduction helper used by both [[cli/execute-command]] and the MCP `ctx_execute` tool documented in [[cli/mcp-command]]. `CommandExecutionReducer` runs the command through an Infrastructure-owned process runner, asks `IExecuteLogIndexFactory` for a dedicated in-memory `TransientExecutionLogDbContext`, streams each emitted line into the DbContext-backed SQLite FTS5 table, then reduces the transcript by querying matching line numbers, expanding each match into a sliding context window, merging overlapping or adjacent intervals, and rehydrating only the merged blocks that fit inside the configured character ceiling. The reducer also enforces a hard indexed-line cap so runaway commands cannot grow the transient transcript without bound; lines beyond the cap are drained but not indexed, and the result is flagged as truncated. When the caller omits `query` or the query returns no matches, the route deliberately returns a compact summary instead of falling back to raw tail output. This keeps the route aligned with the read-side shaping rules in [[architecture/cqrs-pipeline]] while preserving the shared host composition described in [[architecture/host-composition]].

## Components Involved

| Component | Role |
| --- | --- |
| `CommandExecutionReducer` | Shared CLI orchestrator that validates input, selects the working directory, runs the command, merges context windows, and shapes the reduced result. |
| `ICommandProcessRunner` / `SystemCommandRunner` | Infrastructure boundary that parses the raw command string, launches `System.Diagnostics.Process` without shell indirection, redirects and closes stdin, and streams output lines back to the shared reducer. |
| `IExecuteLogIndexFactory` / `IExecuteLogIndex` / `TransientExecutionLogDbContext` / `TransientExecutionLogIndex` | Per-invocation transient search store that opens a dedicated in-memory DbContext, assigns line numbers, executes FTS5 lookups, and rehydrates inclusive line ranges without touching the repository database. |
| `CommandInvocationParser` | Internal tokenizer that splits the user-supplied command string into executable plus arguments while preserving quoted segments. |
| `TokenObjectNotation.SerializeCommandExecutionResult()` | TOON formatter for metadata-first execution output with explicit line ranges and indented reduced blocks. |
| `ExecuteCommand` / `SharpSenseMcpTools.ctx_execute` | Presentation-layer entry points that share the same `CommandExecutionRequest` contract and reduction behavior. |

## Strict Rules

1. Command execution must never use a shell; `SystemCommandRunner` must populate `ProcessStartInfo.ArgumentList` from parsed tokens and keep `UseShellExecute = false`.
2. The execution-log index must stay transient and per invocation; do not persist command transcripts in `SharpSenseDbContext` or the repository SQLite schema.
3. `query` is optional, but when it is missing or yields zero hits the route must return a compact summary only; do not fall back to raw tail output.
4. The reduction algorithm must expand matches into bounded windows, merge overlapping or adjacent ranges, enforce the character ceiling, and cap indexed lines before TOON/JSON presentation code emits the final payload.
5. The process runner must redirect and close stdin immediately after process start so child commands cannot consume MCP stdio transport bytes or block on inherited terminal input.
6. CLI and MCP callers must share the same `CommandExecutionReducer` orchestration path so the command runner, matching behavior, and truncation semantics stay identical across both entry points.
