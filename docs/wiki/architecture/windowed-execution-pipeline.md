---
title: "Windowed Execution Pipeline"
type: architecture
tags: [sqlite, mcp, cqrs, implemented]
created: 2026-05-08
updated: 2026-10-02
confidence: high
---

## The Problem

AI agents need to run real local commands such as builds and audits, but raw terminal telemetry is too large and too noisy to forward directly into model context. If the host returns the full transcript, the caller spends tokens on unfiltered progress chatter instead of the small slices that explain success or failure.

## The Approach

SharpSense now routes command execution through an application command handler used by both [execute command](../cli/execute-command.md) and the MCP `ctx_execute` tool documented in [mcp command](../cli/mcp-command.md). `ExecuteProcessCommandHandler` runs the command through an Infrastructure-owned process runner, asks `IExecuteLogIndexFactory` for a dedicated in-memory `TransientExecutionLogDbContext`, streams each emitted line into the DbContext-backed SQLite FTS5 table, then delegates to the internal `CommandOutputReader` to reduce the transcript by querying matching line numbers, expanding each match into a sliding context window, merging overlapping or adjacent intervals, and rehydrating only the merged blocks that fit inside the configured character ceiling. The process runner enforces shared stdout/stderr limits of 5,000 lines and 1,048,576 UTF-8 bytes of retained line text, excluding line terminators. It reads fixed-size chunks and bounds each pending line before encountering its terminator. The retained prefix and later character-limited excerpts end at Unicode scalar boundaries. Once a limit omits output, remaining output is drained and counted without indexing; the result is flagged as truncated. Reaching a limit exactly at the end of output does not imply truncation. The handler indexes captured callbacks and preserves the runner's observed line count and truncation status even when the query is missing or unmatched. When the caller omits `query` or the query returns no matches, the route deliberately returns a compact summary instead of falling back to raw tail output. This keeps the route aligned with the read-side shaping rules in [cqrs pipeline](cqrs-pipeline.md) while preserving the shared host composition described in [host composition](host-composition.md).

## Components Involved

| Component | Role |
| --- | --- |
| `ExecuteProcessCommandHandler` | Validates input, resolves the working directory, owns the transient index lifetime, and runs the command. |
| `CommandOutputReader` | Reads matching lines from that invocation's index, merges bounded context windows, and shapes the reduced result. |
| `ICommandProcessRunner` / `SystemCommandRunner` | Infrastructure boundary that parses the raw command string, launches `System.Diagnostics.Process` without shell indirection, redirects and closes stdin, and forwards bounded output lines to the handler while draining excess output. |
| `IExecuteLogIndexFactory` / `IExecuteLogIndex` / `TransientExecutionLogDbContext` / `TransientExecutionLogIndex` | Per-invocation transient search store that opens a dedicated in-memory DbContext, assigns line numbers, executes FTS5 lookups, and rehydrates inclusive line ranges without touching the repository database. |
| `CommandInvocationParser` | Internal tokenizer that splits the user-supplied command string into executable plus arguments while preserving quoted segments. |
| `TokenObjectNotation.SerializeCommandExecutionResult()` | TOON formatter for metadata-first execution output with explicit line ranges and indented reduced blocks. |
| `ExecuteCommand` / `SharpSenseMcpTools.ctx_execute` | Presentation-layer entry points that share the same `ExecuteProcessCommand` contract and reduction behavior. |

## Strict Rules

1. Command execution must never use a shell; `SystemCommandRunner` must populate `ProcessStartInfo.ArgumentList` from parsed tokens and keep `UseShellExecute = false`.
2. The execution-log index must stay transient and per invocation; do not persist command transcripts in `SharpSenseDbContext` or the repository SQLite schema.
3. `query` is optional, but when it is missing or yields zero hits the route must return a compact summary only; do not fall back to raw tail output.
4. The reduction algorithm must expand matches into bounded windows, merge overlapping or adjacent ranges, enforce the character ceiling, before TOON/JSON presentation code emits the final payload. The process runner owns capture line/byte limits before any unbounded line allocation.
5. The process runner must redirect and close stdin immediately after process start so child commands cannot consume MCP stdio transport bytes or block on inherited terminal input.
6. CLI and MCP callers must share the same `ExecuteProcessCommandHandler` orchestration path so the command runner, matching behavior, and truncation semantics stay identical across both entry points.

## Process ownership

The runner launches a private supervisor through the CLI host before starting the requested command. The supervisor owns a Unix session/process group or a Windows kill-on-close Job, forwards output with fixed-size buffers, and reports the command's original exit code over a private control pipe. Cancellation, capture failure, or owner disconnect closes that pipe and terminates the owned group, including descendants whose immediate launcher has already exited. Successful completion also cleans up remaining descendants.

This is process-lifetime cleanup, not a security sandbox: Unix commands can deliberately detach into another session. The supervisor requires the installed .NET host already used by the tool; it adds no shell or external helper dependency.
