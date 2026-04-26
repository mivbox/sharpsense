---
title: "Search Command"
type: cli
tags: [spectre, cqrs, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## Command

`sharp-sense search <query>` searches the current repository index and returns ranked code-node hits. `--toon` switches the formatter from the default structured output to the terse TOON form used by automation-friendly flows. Shared bootstrapping follows [[architecture/host-composition]], and the request boundary follows [[architecture/cqrs-pipeline]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `Query` | positional `<query>` | Search text sent to `HybridSearchQuery`. |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace that owns the SQLite index. |
| `UseToonFormat` | `--toon` | Emits TOON-formatted hits instead of the default structured output. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose command-host logging. |

`SearchCommand.Configure()` copies `RepositoryRoot` into `IOptions<SharpSenseCliOptions>`, loads `SharpSenseConfig` from `sharpsense.yaml`, and registers hybrid search, embeddings, and persistence modules.

## Execution Flow

1. `Program.CommandApp.cs` routes `search` to `SearchCommand`.
2. Spectre binds `<query>` and validates that the search text is not blank.
3. `AbstractAsyncCommand<TSettings>` builds the host and applies the shared rules documented in [[architecture/host-composition]].
4. `Configure()` resolves the repository root and registers `AddHybridSearch()`, `AddHybridSearchInfrastructure()`, `AddEmbeddingsInfrastructure()`, and `AddPersistence()`.
5. `Execute()` resolves `IQueryHandler<HybridSearchQuery, HybridSearchResult>` and dispatches a query payload that contains only the search text.
6. `HybridSearchQueryHandler` delegates to `IHybridSearcher`, which blends keyword ranking with persisted vector similarity.
7. The command maps the returned hits into `CodeNodeResult` records and writes either structured output or TOON.
8. TOON now renders the persisted integer handle plus the compact `DisplayName`, for example `[M] \`42\` WorkspaceLoader.Load(string) @ src/...`. The compact handle comes from [[persistence/sqlite-schema]], while the display name comes from the Roslyn/Markdown extraction rules documented in [[extractors/csharp]] and [[extractors/markdown]].
