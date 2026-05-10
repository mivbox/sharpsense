---
title: "Mcp Command"
type: cli
tags: [spectre, mcp, implemented]
created: 2026-04-26
updated: 2026-05-10
confidence: high
---

## Command

`sharp-sense mcp` starts the stdio MCP host for SharpSense tools. It exposes the same indexed read surfaces as [[cli/search-command]], [[cli/trace-command]], [[cli/inheritors-command]], and [[cli/context-command]], the execution-focused `ctx_execute` surface shared with [[cli/execute-command]], plus the write-capable `refactor_symbol` surface shared with [[cli/refactor-command]]. MCP caller tracing still uses the broader `ImpactAnalysisQuery` defaults instead of the CLI trace command's direct-caller shortcut. Shared bootstrapping still follows [[architecture/host-composition]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace used by the MCP tools. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose host logging for the long-lived stdio process. |

`McpCommand.Configure()` writes `RepositoryRoot` into `IOptions<SharpSenseCliOptions>`, loads `SharpSenseConfig`, and composes the feature modules needed by the MCP tool surface.

## Tool Surface

| Tool | Backing query | Purpose |
| --- | --- | --- |
| `ctx_execute` | `CommandExecutionReducer` | Runs a local command, indexes streamed output in a transient DbContext-backed FTS5 store, and returns reduced TOON log excerpts or a compact summary when no query hits are found. |
| `semantic_search` | `HybridSearchQuery` | Hybrid BM25 + vector search over indexed code nodes, formatted as hierarchical directory/file TOON blocks for token-efficient handoff. |
| `trace_node` | `TraceQuery` / `ImpactAnalysisQuery` | Downstream callees or upstream caller blast radius for a known node id, formatted as arrow-chain TOON. |
| `get_inheritors` | `GetInheritorsQuery` | Direct derived classes or interface implementers for a persisted node id. |
| `context` | `IContextService` | Immediate callers, callees, and inheritance breadth for a persisted node id as compressed TOON. |
| `refactor_symbol` | `IRefactorSymbolService` | Semantically rename a persisted symbol, update Roslyn references when applicable, and return compact TOON write results. |

## Example Outputs and Rough Token Cost

These examples use real fixture outputs and rough token estimates based on output length (`~characters / 4`), so expect model-specific variance.

### `ctx_execute`

Approximate output size for this sample: `~90` tokens.

```text
command: dotnet build SharpSense.sln
status: success
exit_code: 0
working_directory: /repo
query: Build succeeded
metrics:
  captured_lines: 1201
  matched_lines: 1
  block_count: 1
  truncated: false
summary: Returned 1 merged block(s) from 1 matched line(s) across 1201 captured line(s).
output:
  - span: 1201-1201
    text: |
      1201| Build succeeded in 13.7s
```

### `semantic_search`

Approximate output size for this sample: `~60` tokens.

```text
src/SharpSense.Infrastructure/DependencyGraph/:
  DependencyGraphMapper.cs:
    - [M] `553` ToExternalGraphNode L20-21
    - [M] `556` ToGraphNode L32-47

src/SharpSense.Domain/KnowledgeGraph/Nodes/:
  ProjectNode.cs:
    - [P] `373` Id L5
```

### `context`

Approximate output size for this sample: `~80` tokens.

```text
node:
  id: 42
  name: PaymentProcessor.ProcessPayment(string, int)
  kind: M
  file: src/Fixture.App/PaymentProcessor.cs:12-30

incoming:
  callers: [HttpEndpoint.Handle (Id:7)]
  implementers: [PaymentProcessorBase (Id:8)]

outgoing:
  callees: [ReceiptWriter.WriteReceipt (Id:9)]
  inherits: [IPaymentProcessor (Id:10)]
```

### `trace_node`

Approximate output size for this sample: `~40` tokens.

```text
- [M] `1` MessageConsumer.Render @ src/Fixture.App/MessageConsumer.cs:L20-28
  -> [M] `3` MessageProvider.GetMessage @ src/Fixture.App/MessageProvider.cs:L7-11
```

### `get_inheritors`

Approximate output size for this sample: `~30` tokens.

```text
[C] `7` DerivedAlpha @ src/Fixture.App/DerivedAlpha.cs:3-16
[C] `8` DerivedBeta @ src/Fixture.App/DerivedBeta.cs:3-17
```

### `refactor_symbol`

Approximate output size for this sample: `~20` tokens.

```text
refactor_success: true
modified_files:
  - src/Fixture.App/PaymentProcessor.cs
  - src/Fixture.App/CheckoutController.cs
```

## Execution Flow

1. `Program.CommandApp.cs` routes `mcp` to `McpCommand`.
2. `AbstractAsyncCommand<TSettings>` builds the host using the shared rules in [[architecture/host-composition]].
3. `Configure()` resolves the repository root and registers repository workspace, `SharpSenseConfig`, command execution, Context360, hybrid search, refactoring, indexing infrastructure, embeddings, inheritors, impact analysis, trace, and persistence.
4. The command adds the MCP server with stdio transport and registers `SharpSenseMcpTools` as the tool surface.
5. Tool serialization adds a `JsonStringEnumConverter<TraceDirection>` so trace directions stay stable across the protocol boundary.
6. `ctx_execute` resolves `ICommandExecutor`, runs the raw command string inside the configured repository root, streams output through the transient FTS5 reducer from [[architecture/windowed-execution-pipeline]], and renders metadata-first TOON through `TokenObjectNotation.SerializeCommandExecutionResult()`.
7. `semantic_search` resolves `HybridSearchQuery` and serializes hits through `TokenObjectNotation.SerializeSemanticSearch()`, the shared hierarchical TOON serializer used by `sharp-sense search --toon`.
8. `trace_node` resolves the root node through `ITraceNavigator`, then formats either direct callees or caller chains through the dedicated trace serializers in `TokenObjectNotation`.
9. `context` resolves `IContextService` directly and renders the shared `Context360Result` through `TokenObjectNotation.SerializeContext360()`.
10. `get_inheritors` resolves `GetInheritorsQuery` through the `IInheritorFinder` read slice and formats direct class inheritors or interface implementers with the shared flat TOON output formatter.
11. `refactor_symbol` resolves `IRefactorSymbolService`, performs a semantic rename against either the Roslyn workspace or the Markdown strategy, and returns `TokenObjectNotation.SerializeRefactorResult()` without waiting for downstream index refresh.
12. `Execute()` waits for the stdio host to shut down while the registered tools resolve queries on demand.
