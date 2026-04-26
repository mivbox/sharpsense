---
title: "SQLite Schema"
type: persistence
tags: [sqlite, embeddings, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## Schema

`SharpSenseDbContext` exposes three entity sets: `ProjectNodes`, `CodeNodes`, and `DependencyEdges`, with model configuration applied from the persistence assembly. `DependencyEdges` are keyed by `(CallerId, CalleeId, EdgeType)`, and `ProjectNodes` enforce a unique index on `RelativeFilePath`.

`CodeNodes` now split identity into two layers:

- `Id INTEGER PRIMARY KEY` is the compact persisted handle used by [[cli/search-command]] and [[cli/trace-command]].
- `CanonicalId TEXT UNIQUE` preserves the deterministic semantic identity that dependency edges and the UI graph still use.
- `DisplayName` stores the namespace-trimmed C# signature or compact document name rendered by TOON and MCP output.

The migrations provision one FTS5 virtual table, `CodeNodeSearch`, with `Id`, `CanonicalId`, `DisplayName`, `FullyQualifiedName`, `Summary`, and `RelativeFilePath`. `Id` and `CanonicalId` are stored but not full-text indexed; lexical ranking uses `DisplayName`, `FullyQualifiedName`, `Summary`, and `RelativeFilePath`. A separate `IX_CodeNodes_FullyQualifiedName_NoCase` index supports case-insensitive fallback lookup for trace identifiers.

Embeddings still live directly in `CodeNodes.VectorEmbedding`, and `HybridSearcher` calculates cosine distance from that column rather than from a separate vec0 table.

## Read/Write Patterns

A full Target index first loads the existing `CanonicalId -> Id` map, applies those ids to newly extracted nodes, clears tracked state, deletes `DependencyEdges`, `CodeNodes`, and `ProjectNodes`, inserts the new graph, and rebuilds `CodeNodeSearch` from `CodeNodes` inside one transaction. This keeps integer ids stable across no-op reindexes even though the full write path still rewrites the table contents.

`HybridSearcher` reads ranked candidates from `CodeNodeSearch`, returns integer ids, and calculates vector similarity directly against `CodeNodes.VectorEmbedding` with `vec_distance_cosine(VectorEmbedding, vec_f32($queryVector))`. `CodeNodeNavigationQueries.FindRootNode()` resolves an incoming trace identifier by trying integer `Id`, then exact `CanonicalId` / `FullyQualifiedName`, then `FullyQualifiedName COLLATE NOCASE`.

## Target Overwrites

Incremental writes are scoped by repository-relative `RelativeFilePath`, not by whole-database resets. `GetAffectedRelativePaths()` normalizes changed file paths through `IRepositoryWorkspace`, filters them to C# and Markdown files, and sorts them with an OS-aware comparer. `UpdateIncremental()` then:

1. Loads the existing `(Id, CanonicalId)` pairs for the changed paths.
2. Reapplies those ids to re-extracted nodes whose `CanonicalId` still matches.
3. Computes removed node ids by comparing persisted canonical ids with the newly extracted batch.
4. Deletes dependency edges rooted in the changed caller ids, plus edges to removed callees.
5. Deletes `CodeNodeSearch` rows and `CodeNodes` rows for the changed `RelativeFilePath` values.
6. Inserts the new `CodeNodes` and `DependencyEdges`.
7. Rebuilds `CodeNodeSearch` rows with `INSERT OR REPLACE`.

This is the persistence half of [[architecture/incremental-watch]], and it relies on the repository-relative paths produced by [[architecture/file-discovery]] and the extractors.
