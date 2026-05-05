---
title: "SQLite Schema"
type: persistence
tags: [sqlite, embeddings, implemented]
created: 2026-04-26
updated: 2026-05-06
confidence: high
---

## Schema

`SharpSenseDbContext` exposes four entity sets: `ProjectNodes`, `CodeNodes`, `WorkspaceTreeNodes`, and `DependencyEdges`, with model configuration applied from the persistence assembly. `DependencyEdges` are keyed by `(CallerId, CalleeId, EdgeType)`, `ProjectNodes` enforce a unique index on `RelativeFilePath`, and `WorkspaceTreeNodes` are indexed by `ParentId`, unique `Path`, and `ProjectId` so the UI can page immediate children efficiently.

`CodeNodes` now split identity into two layers:

- `Id INTEGER PRIMARY KEY` is the compact persisted handle used by [[cli/search-command]] and [[cli/trace-command]].
- `CanonicalId TEXT UNIQUE` preserves the deterministic semantic identity that dependency edges and the UI graph still use.
- `DisplayName` stores the namespace-trimmed C# signature or compact document name rendered by TOON and MCP output.

`WorkspaceTreeNodes` persist the lazy explorer hierarchy produced from analyzed `RelativeFilePath` values. Each row stores the canonical tree `Path`, its `ParentId`, display `Label`, `Kind`, optional owning `ProjectId`, and the `HasChildren` / `ChildCount` metadata needed by the explorer.

`CodeNodes` now persist two semantic-indexing fields in addition to the rendered signature and vector: `SearchText` stores the exact text used to generate embeddings, and `BodyHash` stores the SHA-256 fingerprint of the raw method body for method nodes only. `Summary` remains the extracted human-readable documentation text (`summary`/`remarks`) and never stores raw method bodies.

The migrations provision one FTS5 virtual table, `CodeNodeSearch`, with `Id`, `CanonicalId`, `DisplayName`, `FullyQualifiedName`, `SearchText`, and `RelativeFilePath`. `Id` and `CanonicalId` are stored but not full-text indexed; lexical ranking uses `DisplayName`, `FullyQualifiedName`, `SearchText`, and `RelativeFilePath`. A separate `IX_CodeNodes_FullyQualifiedName_NoCase` index supports case-insensitive fallback lookup for trace identifiers.

Embeddings still live directly in `CodeNodes.VectorEmbedding`, and `HybridSearcher` calculates cosine distance from that column rather than from a separate vec0 table.

## Read/Write Patterns

A full Target index first loads the current persisted snapshot. If the extracted graph is identical after vector reuse, persistence short-circuits and leaves the SQLite file untouched. When the graph has changed, the repository still rebuilds the full persisted graph inside one transaction, preserving integer ids by reapplying the existing `CanonicalId -> Id` map before rewriting `CodeNodes`, `ProjectNodes`, `Documents`, `Directories`, and `CodeNodeSearch`.

`HybridSearcher` reads ranked candidates from `CodeNodeSearch`, returns integer ids, and calculates vector similarity directly against `CodeNodes.VectorEmbedding` with `vec_distance_cosine(VectorEmbedding, vec_f32($queryVector))`. `CodeNodeNavigationQueries.FindRootNode()` resolves an incoming trace identifier by trying integer `Id`, then exact `CanonicalId` / `FullyQualifiedName`, then `FullyQualifiedName COLLATE NOCASE`. `WorkspaceTreeRepository` reads immediate explorer rows from `WorkspaceTreeNodes` without consulting the live filesystem, which keeps [[cli/ui-command]] aligned with [[architecture/workspace-tree]].

## Target Overwrites

Incremental writes are scoped by repository-relative `RelativeFilePath`, not by whole-database resets. `GetAffectedRelativePaths()` normalizes changed file paths through `IRepositoryWorkspace`, filters them to C# and Markdown files, and sorts them with an OS-aware comparer. `UpdateIncremental()` then:

1. Loads the persisted code nodes for the changed repository-relative paths and compares them to the new extraction payload.
2. Reuses stored vector embeddings when both `SearchText` and `BodyHash` still match, so unchanged methods avoid re-embedding.
3. Updates only the changed or newly added `CodeNodes`, deletes removed `CodeNodes`, and keeps untouched rows in place.
4. Replaces dependency edges rooted in the changed caller ids and deletes inbound edges to removed callees.
5. Updates only the affected `CodeNodeSearch` rows instead of rebuilding the full FTS table.
6. Inserts or removes `Documents` / `Directories` only when changed paths add, remove, or rename files.

This is the persistence half of [[architecture/incremental-watch]], and it relies on the repository-relative paths produced by [[architecture/file-discovery]], the explorer pattern described in [[architecture/workspace-tree]], and the extractors.
