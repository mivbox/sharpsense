---
title: "File Discovery"
type: architecture
tags: [csharp, markdown, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## The Problem

Indexing and incremental updates need one canonical way to discover allowed files, anchor include globs to a Target, and hand extractors a normalized repository-relative path. If each component discovers files differently, `.gitignore` handling and relative-path semantics drift apart.

## The Approach

SharpSense makes `IWorkspaceFileDiscoverer` the canonical discovery boundary. `DocumentDiscoverer` reads `IncludePaths` from `SharpSenseConfig`, resolves the Target directory through `IRepositoryWorkspace`, and asks `IWorkspaceFileDiscoverer` for the allowed files. `WorkspaceFileDiscoverer` normalizes the configured globs, matches them under the Target directory, converts each absolute match to a repository-relative path, applies `.gitignore` through the Ignore engine, and emits `DiscoveredFile` records. Downstream components such as [[extractors/markdown]] then index content by using `DiscoveredFile.RelativeFilePath` directly instead of recomputing paths.

## Components Involved

| Component | Role |
| --- | --- |
| `SharpSenseConfig` | Supplies the `IncludePaths` globs for document discovery. |
| `IRepositoryWorkspace` | Resolves the Target directory and converts absolute paths into repository-relative paths. |
| `IWorkspaceFileDiscoverer` | Defines the allowed-file contract for discovery. |
| `WorkspaceFileDiscoverer` | Applies glob matching, `.gitignore`, and separator normalization before returning `DiscoveredFile` records. |
| `DiscoveredFile` | Carries the absolute file path plus the normalized repository-relative path for extractors. |
| `DocumentDiscoverer` | Reuses the allowed-file set and streams markdown content into the indexer. |

## Strict Rules

1. Run file discovery through `IWorkspaceFileDiscoverer` so `.gitignore` and separator normalization stay centralized.
2. Anchor include globs to the active Target directory, not to arbitrary process working directories.
3. Treat `DiscoveredFile.RelativeFilePath` as the canonical repository-relative path; extractors must not recalculate it.
4. Keep discovery scoped to C# and Markdown Targets, because those are the only indexed asset types.
5. The current watch-mode directory-rename path still expands files with direct filesystem enumeration instead of `IWorkspaceFileDiscoverer`. Treat that as a compliance gap to fix, not as the intended pattern for [[architecture/incremental-watch]].
