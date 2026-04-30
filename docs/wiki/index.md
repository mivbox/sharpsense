# SharpSense Knowledge Base – Schema & Linting Rules

## Purpose

This is an LLM-maintained knowledge base for the SharpSense knowledge graph indexer. The project is strictly scoped to
C# (via Roslyn) and Markdown (via Markdig). The LLM writes and maintains all files under `docs/wiki/`. The human curates
raw sources, provides architectural directives, and directs queries. The human never edits wiki files directly.

## Directory Layout

* `docs/` - Immutable source documents (code snippets, raw architectural logs, external API docs like Roslyn). Never
  modify these.
* `docs/wiki/index.md` - Master catalogue & Ruleset (This file). Every wiki page must align with these rules.
* `docs/wiki/log.md` - Append-only activity log of architectural decisions and updates.
* `docs/wiki/architecture/` - Core architectural patterns (CQRS, Options pattern, Watcher loops).
* `docs/wiki/extractors/` - Documentation on language-specific extractors (Roslyn/C#, Markdig/Markdown).
* `docs/wiki/cli/` - Spectre.Console command definitions, options, and routing.
* `docs/wiki/persistence/` - SQLite schema, vector embedding strategies, and incremental upsert logic.

## Master Catalogue

### Architecture

* [[architecture/cqrs-pipeline]] - The strict boundary between `SharpSenseCliOptions` configuration and CQRS payloads.
* [[architecture/host-composition]] - Shared host bootstrapping, configuration binding, and DI composition for CLI routes.
* [[architecture/file-discovery]] - The canonical discovery path for Target files, `.gitignore`, and normalized relative paths.
* [[architecture/incremental-watch]] - Debounced watch-mode batching, recovery rules, and incremental dispatch flow.
* [[architecture/virtual-file-system]] - The Infrastructure-owned `IFileSystem` boundary, allowed physical-edge exceptions, and stateless test rules.
* [[architecture/vertical-slice-application]] - The canonical Application-layer Vertical Slice layout, boundaries, and handler locality rules.
* [[architecture/workspace-tree]] - The persisted workspace-tree read model that powers lazy explorer expansion and opt-in graph scope.

### CLI

* [[cli/analyze-command]] - The `analyze` and `index` routes, option binding, host composition, and watch/update flow.
* [[cli/inheritors-command]] - The `inheritors` route for direct class inheritors and interface implementers.
* [[cli/search-command]] - The `search` route, hybrid-search query flow, and TOON output switch.
* [[cli/trace-command]] - The `trace` route, caller/callee dispatch, and output shaping.
* [[cli/mcp-command]] - The `mcp` route and stdio MCP host composition.
* [[cli/ui-command]] - The `ui` route, embedded asset host, workspace-tree API, and opt-in dependency graph API.

### Extractors

* [[extractors/csharp]] - Roslyn-backed C# extraction, workspace reuse, and incremental document updates.
* [[extractors/markdown]] - Markdig-backed Markdown discovery, chunking, slugging, and link extraction.

### Persistence

* [[persistence/sqlite-schema]] - The SQLite tables, workspace-tree read model, search structures, embeddings storage, and RelativeFilePath overwrite flow.

## File Naming

* All lowercase, hyphens for word separation: `csharp-language-extractor.md`
* No spaces, no special characters, no uppercase.
* Name should match the page title slug.

## Page Format

Every wiki page uses this frontmatter and structure:
---
title: "Page Title"
type: architecture | extractor | cli | persistence
tags: [tag1, tag2, tag3]
created: YYYY-MM-DD
updated: YYYY-MM-DD
confidence: high | medium | low
---

## Required Sections by Page Type

### Architecture pages (`docs/wiki/architecture/`):

* `## The Problem` - What architectural challenge this pattern solves.
* `## The Approach` - Plain-English explanation of the pattern and why it is used.
* `## Components Involved` - Interfaces and concrete classes that implement this.
* `## Strict Rules` - Specific guardrails (e.g. CQRS decoupling, event data vs config).

### Extractor pages (`docs/wiki/extractors/`):

* `## Target Language` - Only C# or Markdown.
* `## Full Index Logic` - How it parses the AST/Document from scratch.
* `## Incremental Logic` - How it handles `--watch` delta updates (The Scalpel).
* `## Dependencies` - External tools (Strictly Roslyn and Markdig).

### CLI pages (`docs/wiki/cli/`):

* `## Command` - The Spectre.Console route.
* `## Options` - Bound properties passed to `SharpSenseCliOptions`.
* `## Execution Flow` - Step-by-step lifecycle from invocation to completion.

### Persistence pages (`docs/wiki/persistence/`):

* `## Schema` - Tables, FTS setup, and Vector column definitions.
* `## Read/Write Patterns` - How data is queried or upserted.
* `## Target Overwrites` - How incremental deletes/inserts are targeted by `RelativeFilePath`.

## Linking Conventions

* Use Obsidian-style wiki links: `[[architecture/cqrs-pipeline]]`
* Always use relative paths from the wiki root.
* Every page must link to at least one other page (no orphans).
* When mentioning a concept that has a page, always link it.

## Tagging Taxonomy

* **Tech:** `csharp`, `markdown`, `roslyn`, `markdig`, `sqlite`, `spectre`, `cqrs`
* **Features:** `incremental-watch`, `embeddings`, `ast-parsing`, `mcp`
* **Status:** `implemented`, `planned`, `deprecated`

## Confidence Levels

* `high` - Fully implemented, tested, and active in the current codebase.
* `medium` - Architected and agreed upon, but implementation may be pending.
* `low` - Speculative idea or upcoming feature.

## SharpSense Strict Code Guardrails

The LLM **must** obey these rules when writing code or documenting architecture for this project:

1. **Scope:** * This project strictly indexes C# and Markdown. Do not suggest or implement TypeScript, Node.js sidecars,
   Python, or external process extraction.
2. **Naming Conventions:** * **NO** "Service" suffixes on class names (e.g., `WorkspaceWatcher`, not
   `WorkspaceWatcherService`).
    * **NO** "Async" suffixes on custom methods (e.g., `Extract()`, not `ExtractAsync()`). Native base class overrides
      are the exception.
3. **Configuration Pattern:** * CLI settings are NEVER passed through CQRS command payloads.
    * Settings (`TargetPath`, `SkipEmbeddings`) go into `IOptions<SharpSenseCliOptions>`.
    * Event Data (`ChangedFiles`) goes into CQRS Command records.
4. **Terminology:** * Never use the term "Solution" (e.g., `SolutionPath`). Always use "Target" or "Workspace" (e.g.,
   `TargetPath`). Even though we are focused on C#, Markdown files exist outside of `.sln` files, making generic
   workspace boundaries necessary.
5. **Comments:** * Do not add XML comments to concrete implementations.
6. **Pathing:** * All file discovery must pass through `IWorkspaceFileDiscoverer` to respect `.gitignore` rules and
   normalize directory separators. Extractors must not manually calculate relative paths; they must use the
   `RelativeFilePath` provided by the `DiscoveredFile` record.
7. **Application Vertical Slices:** * The `src/SharpSense.Application/Features/` root is forbidden.
    * Every Application feature lives directly under `src/SharpSense.Application/{FeatureName}/`.
    * Shared slice interfaces go in `Abstractions/`.
    * Shared slice records go in `Models/`.
    * Each command or query lives under its own `{CommandOrQueryName}/` directory.
    * Command/query-local records go in `{CommandOrQueryName}/Models/`.
    * Feature registration stays in `{FeatureName}ServiceCollectionExtensions.cs`.

## Workflows

### Ingest

When the user provides a new architectural plan or code snippet:

1. Read the raw source completely.
2. Create or update the relevant pages in `docs/wiki/`.
3. Identify all architectural shifts (e.g. renaming variables, shifting responsibilities).
4. Add cross-links in both directions between all touched pages.
5. Update `docs/wiki/index.md` if new catalogue entries are needed.
6. Append to `docs/wiki/log.md` with timestamp, summary of change, and pages updated.

### Query

When the user asks a question about the architecture:

1. Read `docs/wiki/index.md` to find relevant pages.
2. Synthesise an answer citing specific pages with wiki links.
3. Enforce the "SharpSense Strict Code Guardrails" in the response.

### Lint

When the user says "lint" or "health check":

1. Read all wiki pages.
2. Check for: orphan pages, stale claims (e.g. lingering references to `SolutionPath` or `TryApplyChanges`),
   contradictions, and incomplete sections.
3. Fix what can be fixed automatically and report issues needing human judgment.
4. Update `docs/wiki/log.md`.
