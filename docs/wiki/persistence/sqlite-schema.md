---
title: "SQLite Schema"
type: persistence
tags: [sqlite, embeddings, implemented]
created: 2026-04-26
updated: 2026-05-14
confidence: high
---

## Schema

`SharpSenseDbContext` exposes the persisted graph and search entities with model configuration applied from the persistence assembly. `DependencyEdges` are keyed by `(CallerId, CalleeId, EdgeType)`, `ProjectNodes` enforce a unique index on `RelativeFilePath`, and the search/persistence layer now stores both deterministic code rows and persistent semantic memory rows.

`CodeNodes` now split identity into two layers:

- `Id INTEGER PRIMARY KEY` is the compact persisted handle used by [[cli/search-command]] and [[cli/trace-command]].
- `CanonicalId TEXT UNIQUE` preserves the deterministic semantic identity that dependency edges and the UI graph still use.
- `DisplayName` stores the namespace-trimmed C# signature or compact document name rendered by TOON and MCP output.

`MemoryNodes` are the persistent semantic layer for human/AI-authored intent. Each row stores `Guid Id`, `TargetFullyQualifiedName`, a snapshotted `TargetCodeHash`, markdown `Content`, `ContentHash`, JSON `TagsJson`, optional `VectorEmbedding`, and `CreatedAt`. `MemoryNodes` link to `CodeNodes` via a foreign key on `TargetFullyQualifiedName` with `ON DELETE CASCADE`, so deleting a code node (or wiping its `Document` / `ProjectNode` / `GraphNode` in the parser UPSERT path) automatically destroys every memory that was attached to it. The matching `IX_CodeNodes_FullyQualifiedName_NoCase` index is now a `UNIQUE` constraint that doubles as the EF Core alternate key used by the FK principal lookup, so two `CodeNodes` can never share the same `FullyQualifiedName`. `NodeExtractor` enforces the same invariant upstream by keying its per-`Extract` emission cache by `FullyQualifiedName` (which is project-independent after `RoslynSymbolUtilities.Canonicalize` collapses `OriginalDefinition` / `ReducedFrom`), so a single symbol visible in more than one `.csproj` compilation still produces exactly one `CodeNode` per analyze pass. `KnowledgeGraphRepository.UpsertCodeNodes` carries a small defence-in-batch dedup on the same key as a safety net for any future regression in the extractor.

`CodeNodes` now persist two semantic-indexing fields in addition to the rendered signature and vector: `SearchText` stores the exact text used to generate embeddings, and `BodyHash` stores the SHA-256 fingerprint of the declaration syntax after trivia has been stripped. `Summary` remains the extracted human-readable documentation text (`summary`/`remarks`) and never stores raw method bodies.

The migrations provision one FTS5 virtual table, `CodeNodeSearch`, with `Id`, `CanonicalId`, `DisplayName`, `FullyQualifiedName`, `SearchText`, and `RelativeFilePath`. `Id` and `CanonicalId` are stored but not full-text indexed; lexical ranking uses `DisplayName`, `FullyQualifiedName`, `SearchText`, and `RelativeFilePath`. A separate `IX_CodeNodes_FullyQualifiedName_NoCase` index supports case-insensitive fallback lookup for trace identifiers.

Embeddings still live directly in `CodeNodes.VectorEmbedding`, and `HybridSearcher` calculates cosine distance from that column rather than from a separate vec0 table.

## Read/Write Patterns

A full Target index first loads the current persisted snapshot. If the extracted graph is identical after vector reuse, persistence short-circuits and leaves the SQLite file untouched. When the graph has changed, the repository still rebuilds the full persisted graph inside one transaction, preserving integer ids by reapplying the existing `CanonicalId -> Id` map before rewriting `CodeNodes`, `ProjectNodes`, `Documents`, `Directories`, and `CodeNodeSearch`.

`HybridSearcher` reads ranked candidates from `CodeNodeSearch`, returns integer ids, and calculates vector similarity directly against `CodeNodes.VectorEmbedding` with `vec_distance_cosine(VectorEmbedding, vec_f32($queryVector))`. When `IncludeMemories` is enabled, the same search path can also widen candidate discovery through `MemoryNodes`, pre-filter memory rows with `json_each(TagsJson)`, and merge their keyword/vector relevance back onto the owning code-node id. `CodeNodeNavigationQueries.FindRootNode()` resolves an incoming trace identifier by trying integer `Id`, then exact `CanonicalId` / `FullyQualifiedName`, then `FullyQualifiedName COLLATE NOCASE`. `WorkspaceTreeRepository` reads immediate explorer rows from `WorkspaceTreeNodes` without consulting the live filesystem, which keeps [[cli/ui-command]] aligned with [[architecture/workspace-tree]].

## Target Overwrites

Incremental writes are scoped by repository-relative `RelativeFilePath`, not by whole-database resets. `GetAffectedRelativePaths()` normalizes changed file paths through `IRepositoryWorkspace`, filters them to C# and Markdown files, and sorts them with an OS-aware comparer. `UpdateIncremental()` then:

1. Loads the persisted code nodes for the changed repository-relative paths and compares them to the new extraction payload.
2. Reuses stored vector embeddings when both `SearchText` and `BodyHash` still match, so unchanged methods avoid re-embedding.
3. Updates only the changed or newly added `CodeNodes`, deletes removed `CodeNodes`, and keeps untouched rows in place.
4. Replaces dependency edges rooted in the changed caller ids and deletes inbound edges to removed callees.
5. Updates only the affected `CodeNodeSearch` rows instead of rebuilding the full FTS table.
6. Inserts or removes `Documents` / `Directories` only when changed paths add, remove, or rename files.

`AttachMemoryCommand` writes `MemoryNodes` independently of the indexing overwrite path. Memory writes reuse existing embeddings when `ContentHash` already exists, but memory staleness is computed lazily at read time by comparing the current `CodeNodes.BodyHash` to the persisted `TargetCodeHash`.

This is the persistence half of [[architecture/incremental-watch]], and it relies on the repository-relative paths produced by [[architecture/file-discovery]], the explorer pattern described in [[architecture/workspace-tree]], and the extractors.
