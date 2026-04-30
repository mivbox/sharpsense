---
title: "CQRS Pipeline"
type: architecture
tags: [cqrs, spectre, implemented]
created: 2026-04-26
updated: 2026-04-30
confidence: high
---

## The Problem

SharpSense has to move three different kinds of data through the same runtime: host configuration, command-side event batches, and query-side request payloads. If those concerns leak into each other, CLI routes become hard to compose, handlers gain orchestration logic, and incremental indexing stops looking like pure CQRS traffic.

## The Approach

SharpSense binds long-lived command configuration once during [[architecture/host-composition]] by writing CLI values into `IOptions<SharpSenseCliOptions>` and loading `sharpsense.yaml` into `SharpSenseConfig`. The CQRS records stay narrow on both sides of the boundary:

* `IndexTargetCommand` carries only full-index runtime signals (`Progress` and `EmbeddingProgress`).
* `UpdateWorkspaceFilesCommand` carries only incremental runtime signals (`ChangedFiles` and `Progress`).
* `HybridSearchQuery` carries only the search text and optional query filters.
* `TraceQuery`, `ImpactAnalysisQuery`, `GetDependencyGraphNodesQuery`, and `GetDependencyGraphEdgesQuery` carry only the traversal or hydration request data needed for the current call.

`ICommandHandler<TCommand>` and `IQueryHandler<TQuery, TResult>` implementations stay thin and delegate to feature orchestrators such as `IKnowledgeGraphIndexing`, `IHybridSearcher`, `ITraceNavigator`, `IImpactAnalyzer`, and `IDependencyGraphRepository`. That keeps the public routes in [[cli/analyze-command]], [[cli/search-command]], [[cli/trace-command]], and [[cli/ui-command]] stable even as the underlying infrastructure changes.

## Components Involved

| Component | Role |
| --- | --- |
| `SharpSenseCliOptions` / `SharpSenseConfig` | Host-scoped configuration for repository roots, Target paths, watch flags, embeddings switches, and include paths. |
| `ICommandHandler<TCommand>` | Minimal command-side abstraction used for mutation flows such as full and incremental indexing. |
| `IQueryHandler<TQuery, TResult>` | Minimal query-side abstraction used for read flows such as search, trace, impact analysis, and dependency graph retrieval. |
| `IndexTargetCommand` / `UpdateWorkspaceFilesCommand` | Write-side records that carry only progress and changed-file event data. |
| `HybridSearchQuery` / `TraceQuery` / `ImpactAnalysisQuery` / `GetDependencyGraphNodesQuery` / `GetDependencyGraphEdgesQuery` | Read-side records that carry only the request data for the current lookup. |
| `IndexTargetCommandHandler`, `UpdateWorkspaceFilesCommandHandler`, and the query handlers | Thin adapters that forward records to the relevant feature orchestrator. |
| `KnowledgeGraphIndexing` | Scoped orchestration component that consumes extractors, persistence, repository workspace, embeddings, and `IOptions<SharpSenseCliOptions>`. |

## Strict Rules

1. Keep host configuration in `IOptions<SharpSenseCliOptions>` or `SharpSenseConfig`. `TargetPath`, `RepositoryRoot`, `Watch`, `SkipEmbeddings`, and include-path settings must not be copied into CQRS command or query records.
2. Keep command payloads limited to runtime event data such as `ChangedFiles`, `Progress`, and `EmbeddingProgress`.
3. Keep query payloads limited to the current request data such as search text, identifiers, depth limits, and optional filters.
4. Keep handlers thin and register them through modular `Add*` extension methods instead of duplicating orchestration logic in the route or handler body.
5. Use "Target" or "Workspace" terminology when documenting indexed boundaries.
6. Split large read models into multiple query records when transport cost differs by shape; in the UI graph flow, node hydration and edge hydration must stay independently addressable.
