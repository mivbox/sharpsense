---
title: "Mcp Command"
type: cli
tags: [spectre, mcp, implemented]
created: 2026-04-26
updated: 2026-05-02
confidence: high
---

## Command

`sharp-sense mcp` starts the stdio MCP host for SharpSense tools. It exposes the same indexed read surfaces as [[cli/search-command]], [[cli/trace-command]], [[cli/inheritors-command]], and [[cli/context-command]], plus the write-capable `refactor_node` surface shared with [[cli/refactor-command]]. MCP caller tracing still uses the broader `ImpactAnalysisQuery` defaults instead of the CLI trace command's direct-caller shortcut. Shared bootstrapping still follows [[architecture/host-composition]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace used by the MCP tools. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose host logging for the long-lived stdio process. |

`McpCommand.Configure()` writes `RepositoryRoot` into `IOptions<SharpSenseCliOptions>`, loads `SharpSenseConfig`, and composes the feature modules needed by the MCP tool surface.

## Tool Surface

| Tool | Backing query | Purpose |
| --- | --- | --- |
| `semantic_search` | `HybridSearchQuery` | Hybrid BM25 + vector search over indexed code nodes, formatted as hierarchical directory/file TOON blocks for token-efficient handoff. |
| `trace_node` | `TraceQuery` / `ImpactAnalysisQuery` | Downstream callees or upstream caller blast radius for a known node id, formatted as arrow-chain TOON. |
| `get_inheritors` | `GetInheritorsQuery` | Direct derived classes or interface implementers for a persisted node id. |
| `context` | `IContextService` | Immediate callers, callees, and inheritance breadth for a persisted node id as compressed TOON. |
| `refactor_node` | `INodeRefactorer` | Replace a persisted node span through the shared Application refactor boundary and return compact TOON write results. |

## Example Outputs and Rough Token Cost

These examples use real fixture outputs and rough token estimates based on output length (`~characters / 4`), so expect model-specific variance.

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

### `refactor_node`

Approximate output size for this sample: `~20` tokens.

```text
refactor_success: true
modified_files:
  - src/Fixture.App/PaymentProcessor.cs
```

## Execution Flow

1. `Program.CommandApp.cs` routes `mcp` to `McpCommand`.
2. `AbstractAsyncCommand<TSettings>` builds the host using the shared rules in [[architecture/host-composition]].
3. `Configure()` resolves the repository root and registers repository workspace, `SharpSenseConfig`, Context360, hybrid search, refactoring, indexing infrastructure, embeddings, inheritors, impact analysis, trace, and persistence.
4. The command adds the MCP server with stdio transport and registers `SharpSenseMcpTools` as the tool surface.
5. Tool serialization adds a `JsonStringEnumConverter<TraceDirection>` so trace directions stay stable across the protocol boundary.
6. `semantic_search` resolves `HybridSearchQuery` and serializes hits through `TokenObjectNotation.SerializeSemanticSearch()`, the shared hierarchical TOON serializer used by `sharp-sense search --toon`.
7. `trace_node` resolves the root node through `ITraceNavigator`, then formats either direct callees or caller chains through the dedicated trace serializers in `TokenObjectNotation`.
8. `context` resolves `IContextService` directly and renders the shared `Context360Result` through `TokenObjectNotation.SerializeContext360()`.
9. `get_inheritors` resolves `GetInheritorsQuery` through the `IInheritorFinder` read slice and formats direct class inheritors or interface implementers with the shared flat TOON output formatter.
10. `refactor_node` resolves `INodeRefactorer`, writes the Roslyn edit to disk, and returns `TokenObjectNotation.SerializeRefactorResult()` without waiting for downstream index refresh.
11. `Execute()` waits for the stdio host to shut down while the registered tools resolve queries on demand.
