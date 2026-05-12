---
title: "Trace Command"
type: cli
tags: [spectre, cqrs, implemented]
created: 2026-04-26
updated: 2026-05-12
confidence: high
---

## Command

`sharp-sense trace <identifier>` follows relationships that already exist in the index. `-d caller` runs the impact-analysis path, while `-d callee` runs the trace-navigation path. Structural `ParentOf` edges are excluded by default so traces stay on functional relationships unless the caller explicitly opts into structural traversal. Shared bootstrapping follows [[architecture/host-composition]], and the caller/callee split is part of the read-side boundary described in [[architecture/cqrs-pipeline]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `Identifier` | positional `<identifier>` | Node identifier to trace. This can be the persisted integer `Id`, the semantic `CanonicalId`, or a fully qualified name. |
| `Direction` | `-d\|--direction <DIRECTION>` | Chooses `caller` or `callee` traversal. |
| `UseToonFormat` | `--toon` | Emits TOON-formatted trace results. |
| `IncludeStructural` | `--include-structural` | Includes `ParentOf` hierarchy edges instead of staying on functional-only traversal. |
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
7. Both traversal paths now default to functional edge types only, excluding structural `ParentOf` edges unless `--include-structural` is set.
8. Root-node lookup first tries integer `Id`, then exact `CanonicalId` / fully qualified name, then a case-insensitive fully qualified-name fallback backed by [[persistence/sqlite-schema]].
9. The result nodes are mapped into `CodeNodeResult` records and written as structured output or TOON.
10. TOON routes trace results through the dedicated trace serializers in `TokenObjectNotation`, which include the resolved root node, strip method parameter lists from rendered method names, and emit arrow chains such as `- [M] \`42\` WorkspaceLoader.Load @ src/File.cs:L10-20` followed by indented `->` steps for each downstream or upstream hop.
