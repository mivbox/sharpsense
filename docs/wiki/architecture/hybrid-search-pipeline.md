---
title: "Hybrid Search Pipeline"
type: architecture
tags: [sqlite, cqrs, embeddings, implemented]
created: 2026-05-11
updated: 2026-05-14
confidence: high
---

## The Problem

The `search` route has to combine repository filters, SQLite FTS candidate lookup, vector similarity, optional semantic
memory enrichment, and deterministic result ranking. If one class owns every step, the read path becomes hard to extend
and SQLite-specific query concerns start leaking into orchestration code.

## The Approach

SharpSense keeps `HybridSearcher` as the orchestrator for query validation, project and node-type filtering, candidate
hydration, optional memory enrichment, and final hit shaping, but it delegates the SQLite-specific parts of the pipeline
to dedicated collaborators. `IKeywordCandidateProvider` owns the FTS `MATCH` query against `CodeNodeSearch` plus the
memory-backed candidate expansion path, `IVectorScorer` owns the `vec_distance_cosine` lookups against `CodeNodes` and
memory embeddings, and `HybridScoringExtensions` applies the blended keyword-plus-vector ranking before
`HybridSearchMapper` turns the ordered nodes into hits for [[cli/search-command]]. Memory relevance always maps back to
the owning code node, and optional tag filters are pushed into SQLite with `json_each(MemoryNodes.TagsJson)` before any
memory vector-distance calculation. This keeps the read boundary consistent with [[architecture/cqrs-pipeline]] while
preserving the modular host registration pattern described in [[architecture/host-composition]].

## Components Involved

| Component | Role |
| --- | --- |
| `HybridSearcher` | Coordinates request validation, repository filters, candidate hydration, optional embedding generation, and final hit mapping. |
| `IKeywordCandidateProvider` / `SqliteKeywordCandidateProvider` | Builds the FTS token query, executes the `CodeNodeSearch MATCH` lookup, and can union memory-backed candidate node ids when `IncludeMemories` is enabled. |
| `IVectorScorer` / `SqliteVectorScorer` | Loads cosine-distance scores for hydrated candidates that have persisted embeddings, including optional memory-vector matches grouped back onto owning code nodes. |
| `IMemoryReader` | Supplies stale-aware memory payloads for code nodes so ranking can consider memory text without changing the public hit shape. |
| `HybridScoringExtensions` | Computes keyword weights across code-node fields plus optional attached memories, blends vector scores, and applies deterministic tie-break ordering. |
| `HybridSearchInfrastructureServiceCollectionExtensions` | Registers the pipeline abstractions and SQLite-backed implementations for the Infrastructure layer. |

## Strict Rules

1. Keep `HybridSearcher` focused on orchestration; do not move raw SQLite FTS or vector SQL back into the orchestrator.
2. Run keyword candidate selection before vector scoring so embeddings are generated only when hydrated candidates
   actually contain vectors.
3. Reuse one `SharpSenseDbContext` for the full request so repository filters, FTS lookups, and vector lookups observe
   the same indexed state.
4. Preserve the final ranking contract: keyword score plus weighted vector score, then deterministic
   fully-qualified-name and id tie-breakers.
5. Register the pipeline through `AddHybridSearchInfrastructure()` instead of adding one-off search registrations in
   `Program.cs`.
6. Keep memory-aware search opt-in and map semantic hits back onto the owning code node instead of introducing a second
   external handle shape on the search surface.
7. Apply `TagFilters` inside SQLite with `json_each` before memory vector scoring so semantic narrowing stays
   deterministic and cheap.
