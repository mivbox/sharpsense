---
title: "Markdown"
type: extractor
tags: [markdown, markdig, implemented]
created: 2026-04-26
updated: 2026-04-28
confidence: high
---

## Target Language

Markdown. `MarkdownDocumentExtractor` combines the discovery pipeline from [[architecture/file-discovery]] with `IMarkdownIndexer` so Markdown Targets become document nodes and document edges without hard-coding physical file reads into the extractor itself.

## Full Index Logic

1. `MarkdownDocumentExtractor.Extract()` calls `DocumentDiscoverer.Discover(targetPath)`.
2. `DocumentDiscoverer` reads `IncludePaths` from `SharpSenseConfig`, resolves the Target directory, and asks `IWorkspaceFileDiscoverer` for the allowed files. With `docs/**/*.md`, nested wiki pages are part of the same discovery pass as top-level docs.
3. Each `DiscoveredFile` is read through `IFileSystem` on the [[architecture/virtual-file-system]] seam and passed to `IMarkdownIndexer.Index(rawText, relativeFilePath)`.
4. `MarkdownIndexer` parses with Markdig advanced extensions, creates a document-root chunk, splits new chunks at headings, generates stable slugs, and captures summaries from the raw text spans.
5. Each document node gets a compact `DisplayName`: top-level docs drop the `docs/` prefix, and wiki docs drop the `docs/wiki/` prefix so CLI output stays terse while `CanonicalId` remains deterministic.
6. It emits `DocumentHierarchy` edges from heading nesting and `DocumentLink` edges for local Markdown links.
7. Standard Markdown links still resolve relative to the current file, but Obsidian-style wiki links now support `[[page]]`, `[[page#heading]]`, and `[[page|alias]]`. Plain wiki targets inside `docs/wiki/` resolve from the wiki root, while explicit `./` and `../` targets stay relative to the current page.

## Incremental Logic

1. `ExtractIncremental()` filters changed files to Markdown extensions and deduplicates them by current path.
2. `DocumentDiscoverer.DiscoverFiles()` reuses the allowed-file set from `IWorkspaceFileDiscoverer` and keeps only the requested changed files.
3. `DocumentDiscoverer` re-reads only the requested Markdown payloads through `IFileSystem`, leaving file discovery and Markdown parsing separately mockable.
4. Reindexed Markdown nodes reuse the same persisted integer ids when their `CanonicalId` survives the rewrite, so watch-mode updates do not churn the lightweight handles used by [[cli/search-command]] and [[cli/trace-command]].
5. The reindexed document nodes and edges flow through the same RelativeFilePath-targeted overwrite path used by [[persistence/sqlite-schema]].
6. Watch-mode batching and recovery are coordinated by [[architecture/incremental-watch]] rather than by the extractor itself.

## Dependencies

- Markdig `MarkdownPipeline` with advanced extensions.
- `DocumentDiscoverer`, `IWorkspaceFileDiscoverer`, and `IFileSystem` for allow-listed discovery plus file reads.
- `SharpSenseConfig.IncludePaths` for scoped Markdown discovery.
- `IMarkdownIndexer` / `MarkdownIndexer` helpers for slugging, heading hierarchy, wiki-link normalization, and normalized local-link targets.
