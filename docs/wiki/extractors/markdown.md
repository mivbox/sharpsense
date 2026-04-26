---
title: "Markdown"
type: extractor
tags: [markdown, markdig, implemented]
created: 2026-04-26
updated: 2026-04-26
confidence: high
---

## Target Language

Markdown. `MarkdownDocumentExtractor` combines the discovery pipeline from [[architecture/file-discovery]] with `MarkdownIndexer` so Markdown Targets become document nodes and document edges.

## Full Index Logic

1. `MarkdownDocumentExtractor.Extract()` calls `DocumentDiscoverer.Discover(targetPath)`.
2. `DocumentDiscoverer` reads `IncludePaths` from `SharpSenseConfig`, resolves the Target directory, and asks `IWorkspaceFileDiscoverer` for the allowed files. With `docs/**/*.md`, nested wiki pages are part of the same discovery pass as top-level docs.
3. Each `DiscoveredFile` is read and passed to `MarkdownIndexer.Index(rawText, relativeFilePath)`.
4. `MarkdownIndexer` parses with Markdig advanced extensions, creates a document-root chunk, splits new chunks at headings, generates stable slugs, and captures summaries from the raw text spans.
5. Each document node gets a compact `DisplayName`: top-level docs drop the `docs/` prefix, and wiki docs drop the `docs/wiki/` prefix so CLI output stays terse while `CanonicalId` remains deterministic.
6. It emits `DocumentHierarchy` edges from heading nesting and `DocumentLink` edges for local Markdown links.
7. Standard Markdown links still resolve relative to the current file, but Obsidian-style wiki links now support `[[page]]`, `[[page#heading]]`, and `[[page|alias]]`. Plain wiki targets inside `docs/wiki/` resolve from the wiki root, while explicit `./` and `../` targets stay relative to the current page.

## Incremental Logic

1. `ExtractIncremental()` filters changed files to Markdown extensions and deduplicates them by current path.
2. `DocumentDiscoverer.DiscoverFiles()` reuses the allowed-file set from `IWorkspaceFileDiscoverer` and keeps only the requested changed files.
3. Reindexed Markdown nodes reuse the same persisted integer ids when their `CanonicalId` survives the rewrite, so watch-mode updates do not churn the lightweight handles used by [[cli/search-command]] and [[cli/trace-command]].
4. The reindexed document nodes and edges flow through the same RelativeFilePath-targeted overwrite path used by [[persistence/sqlite-schema]].
5. Watch-mode batching and recovery are coordinated by [[architecture/incremental-watch]] rather than by the extractor itself.

## Dependencies

- Markdig `MarkdownPipeline` with advanced extensions.
- `DocumentDiscoverer` and `IWorkspaceFileDiscoverer` for allow-listed discovery.
- `SharpSenseConfig.IncludePaths` for scoped Markdown discovery.
- `MarkdownIndexer` helpers for slugging, heading hierarchy, wiki-link normalization, and normalized local-link targets.
