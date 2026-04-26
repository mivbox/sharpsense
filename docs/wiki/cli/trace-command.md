---
title: "Trace Command"
type: cli
tags: [spectre, cqrs, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## Command

`sharp-sense trace <identifier>` follows relationships that already exist in the index. `-d caller` runs the impact-analysis path, while `-d callee` runs the trace-navigation path. Shared bootstrapping follows [[architecture/host-composition]], and the caller/callee split is part of the read-side boundary described in [[architecture/cqrs-pipeline]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `Identifier` | positional `<identifier>` | Node identifier to trace. This can be the persisted integer `Id`, the semantic `CanonicalId`, or a fully qualified name. |
| `Direction` | `-d\|--direction <DIRECTION>` | Chooses `caller` or `callee` traversal. |
| `UseToonFormat` | `--toon` | Emits TOON-formatted trace results. |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace that owns the SQLite index. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose command-host logging. |

`TraceCommand.Configure()` copies `RepositoryRoot` into `IOptions<SharpSenseCliOptions>`, loads `SharpSenseConfig`, and registers the impact-analysis, trace, and persistence feature slices.

## Execution Flow

1. `Program.CommandApp.cs` routes `trace` to `TraceCommand`.
2. Spectre binds the identifier, validates the direction, and rejects anything other than `caller` or `callee`.
3. `AbstractAsyncCommand<TSettings>` builds the host using the shared rules in [[architecture/host-composition]].
4. `Configure()` resolves the repository root and registers `AddImpactAnalysis()`, `AddImpactAnalysisInfrastructure()`, `AddTrace()`, `AddTraceInfrastructure()`, and `AddPersistence()`.
5. `Execute()` normalizes the requested direction and picks the matching query handler.
6. `caller` dispatches `ImpactAnalysisQuery` with `MaxDepth: 1` and `IncludeTransitive: false`; `callee` dispatches `TraceQuery`.
7. Root-node lookup first tries integer `Id`, then exact `CanonicalId` / fully qualified name, then a case-insensitive fully qualified-name fallback backed by [[persistence/sqlite-schema]].
8. The result nodes are mapped into `CodeNodeResult` records and written as structured output or TOON.
9. TOON uses the same compact formatter as [[cli/search-command]]: backticked integer ids plus `DisplayName` and source span, while dependency traversal still follows canonical string identities under the hood.
