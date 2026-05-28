---
title: "Search Command"
type: cli
tags: [spectre, cqrs, implemented]
created: 2026-04-26
updated: 2026-05-14
confidence: high
---

## Command

`sharp-sense search <query>` searches the current repository index and returns ranked code-node hits. `--toon` switches the formatter from the default structured output to a directory-first, Markdown-friendly TOON form used by automation-friendly flows, and `--include-memories` widens the retrieval scope so attached semantic memory can influence ranking without changing the persisted code-node handle contract. Shared bootstrapping follows [[architecture/host-composition]], and the request boundary follows [[architecture/cqrs-pipeline]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `Query` | positional `<query>` | Search text sent to `HybridSearchQuery`. |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace that owns the SQLite index. |
| `UseToonFormat` | `--toon` | Emits TOON-formatted hits instead of the default structured output. |
| `IncludeMemories` | `--include-memories` | Allows attached `MemoryNodes` to contribute keyword and vector relevance for the owning code node. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose command-host logging. |

`SearchCommand.Configure()` copies `RepositoryRoot` into `IOptions<SharpSenseCliOptions>`, loads `SharpSenseConfig`
from `sharpsense.yaml`, and registers the hybrid-search pipeline described in [[architecture/hybrid-search-pipeline]]
alongside embeddings, memory infrastructure, and persistence modules.

## Execution Flow

1. `Program.CommandApp.cs` routes `search` to `SearchCommand`.
2. Spectre binds `<query>` and validates that the search text is not blank.
3. `AbstractAsyncCommand<TSettings>` builds the host and applies the shared rules documented in [[architecture/host-composition]].
4. `Configure()` resolves the repository root and registers `AddHybridSearch()`, `AddHybridSearchInfrastructure()`, `AddEmbeddingsInfrastructure()`, `AddMemoryInfrastructure()`, and `AddPersistence()`.
5. `Execute()` resolves `IQueryHandler<HybridSearchQuery, HybridSearchResult>` and dispatches a query payload that carries the search text plus the opt-in `IncludeMemories` flag.
6. `HybridSearchQueryHandler` delegates to `IHybridSearcher`, which follows the split documented in [[architecture/hybrid-search-pipeline]]: keyword candidates are loaded first, vector scores are computed only for hydrated candidates, and the final rank blends structural code-node signals with optional memory-backed relevance when `--include-memories` is present.
7. `Execute()` writes either the default structured JSON output or hierarchical TOON.
8. Hierarchical TOON routes the native `HybridSearchHit` array through `TokenObjectNotation.SerializeSemanticSearch()`, which groups hits by directory then file, strips method parameter lists from rendered method names, and emits blocks such as `src/Feature/:`, `  File.cs:`, and `    - [M] \`42\` WorkspaceLoader.Load L10-20`. The persisted handle still comes from [[persistence/sqlite-schema]], while the display name comes from the Roslyn/Markdown extraction rules documented in [[extractors/csharp]] and [[extractors/markdown]].
