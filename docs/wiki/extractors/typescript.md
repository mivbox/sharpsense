---
title: "TypeScript Extraction"
type: architecture
tags: [typescript, indexing, dotnet]
created: 2026-09-24
updated: 2026-10-16
confidence: high
---

# TypeScript extraction

TypeScript and TSX sources use the managed `SharpSense.TypeScript` parser. Select a tsconfig file or directory in a [workspace](../cli/workspace-command.md):

```bash
sharpsense workspace create frontend --repo-root /path/to/repository \
  --typescript frontend/tsconfig.json --markdown 'docs/frontend/**/*.md'
sharpsense analyze --workspace frontend
```

## Discovery and relationships

The extractor supports `.ts` and `.tsx` source files. It uses supported tsconfig membership, extended configs, project references, and path aliases, and follows repository-local imports and re-exports. Shared imported files can therefore sit outside the initially selected folder. Dependency and generated-output directories are excluded.

The parser produces typed syntax nodes. Infrastructure extracts exported declarations and members, parent relationships, imports and re-exports, TSX component references, and statically known fetch/axios HTTP requests. Selected TypeScript sources are merged with C# and Markdown results before a single workspace graph commit.

This is not the TypeScript compiler's type checker. Dynamic values, complex type inference, overload resolution, runtime dependency injection, and inferred cross-language HTTP links are not guaranteed to resolve. JavaScript and `.mts`/`.cts` are not currently included by source discovery.

## Watching

Changes to source, imports, aliases, and configuration can affect consumers throughout the selected graph. Watch re-evaluates the source plan and replaces the complete workspace snapshot after successful extraction. Missing required source folders or configuration failures do not silently replace a valid graph with an empty one.

## Parser architecture

`src/SharpSense.TypeScript` is a dependency-free .NET 10 library imported from Serenity's C# port of the TypeScript scanner and parser. It builds with the ordinary solution; TypeScript analysis has no native parser libraries or separate build workflow.

- `Parsing/TypeScriptParser` owns each parse operation and parser state. It returns a syntax tree with structured diagnostics.
- `Parsing/Models` contains syntax nodes, diagnostics and UTF-16 source positions; `Parsing/Abstractions` contains their shared contracts.
- `Scanning` owns tokenization and its implementation helpers.
- `Infrastructure/Indexing/TypeScript` continues to own file discovery, tsconfig resolution, graph extraction and persistence-facing models. Application orchestration stays in the existing indexing handlers.
- `tests/SharpSense.TypeScript.Tests` covers parser behavior separately from the existing extraction and host tests.

Discovery reads and parses each reachable file once. It returns the source text and syntax tree together for the extraction passes to reuse. These snapshots belong to that analysis; no syntax trees are retained in scoped services between analyses. Changes made after discovery are read by the next analysis. Node identities, parent edges, parsed-file paths and HTTP caller groups use lookup indexes. Documentation extraction uses slices over the syntax tree's line offsets instead of splitting or copying whole files. Repeated identifier text is shared within a parse; its lookup is released when parsing finishes.

Syntax properties and node collections expose read access to consumers; parser mutation stays internal. Optional syntax such as side-effect import clauses, generic arguments, bare return/yield expressions and omitted catch bindings is nullable. Scanner flags, incremental cursor contracts and diagnostic construction remain implementation details; consumers receive immutable `ParseDiagnostic` values.

The imported source revision and TypeScript baseline are recorded in [UPSTREAM.md](../../../src/SharpSense.TypeScript/UPSTREAM.md). The recorded TypeScript baseline is July 2024; recent Serenity maintenance is not evidence of complete support for later TypeScript syntax.

## Parser compatibility and failures

Compatibility coverage includes bare JSX ampersands, type-only star and namespace exports, `typeof import(...)` in generic calls, variance modifiers and contextual parameter names. Dedicated tests also exercise imports, modern declaration forms, source ranges, concurrent calls, cancellation and malformed input.

Every discovered file is parsed before extraction starts. A syntax error fails the complete analysis, preserving the previous graph, edges, hashes and last successful index record. Full and watch analysis follow the same rule. Parse state is isolated per invocation and released even when parsing fails or is cancelled.

`TypeScriptParseOptions` bounds each file to 16,777,216 UTF-16 code units, 1,000,000 completed nodes (including speculative work), and 256 active grammar productions by default. The recursion setting may be reduced or raised up to 512; a runtime stack guard can stop earlier on a constrained thread. These are parser resource limits, not TypeScript syntax restrictions. Exceeding a limit throws `TypeScriptParseLimitException` with the limit name and source offset. Infrastructure reports the affected file and fails the analysis before extraction or graph replacement. Cancellation is checked inside scanner loops, grammar productions, node completion and tree traversal. Parent linking, descendant traversal and parse-error aggregation use explicit stacks.

The parser returns syntax trees and diagnostics; the extraction adapter maps expected source-read, configuration and parser-limit failures to FluentResults. Invalid `extends` values and circular configuration inheritance are reported as configuration failures. This mapping only surrounds source discovery. Unexpected discovery failures, extraction-pass failures and progress-observer exceptions propagate unchanged, as does cancellation, including cancellation from a dependency's token. A cancelled request is checked before validating whether its target exists.

Failures report the first error in each affected file with its repository-relative path, one-based line, UTF-16 editor column, TypeScript diagnostic code and message. At most 49 file diagnostics follow a summary containing the total failed-file count and any omitted count. These messages are retained in the last-attempt diagnostics shown by doctor and the UI. The parser library retains errors at distinct source locations and suppresses duplicate diagnostics at the same location.

A parser error can mean invalid source or unsupported valid syntax. Check the file with the project's TypeScript compiler before changing it; report valid syntax failures with the SharpSense version and a minimal source example. This library performs syntax analysis, not TypeScript type checking.

The import corrects reversed conditions in diagnostic deduplication and child-error aggregation, composite scanner flag checks, and import-type attribute trailing commas. Variance order and duplicate errors use the compiler's TS1029/TS1030 codes. When a type and value share a name, the graph's single identity represents the value declaration consistently, independent of source order. Some body hashes and end positions change because the managed syntax tree uses different declaration boundaries; a full analysis refreshes those records.

Use [doctor](../cli/doctor-command.md) to run the managed TypeScript/TSX compatibility checks and inspect recorded indexing diagnostics. The installed-package integration test covers analysis, real embeddings and graph preservation across failure and recovery. See [source discovery](../architecture/file-discovery.md) and [watch reconciliation](../architecture/incremental-watch.md).

## Compiler compatibility baselines

`tests/SharpSense.TypeScript.Tests/Compatibility/manifest.json` pins TypeScript 6.0.3 and upstream commit `050880ce59e30b356b686bd3144efe24f875ebc8`. The checked-in corpus contains 60 TypeScript/TSX source units from 48 official compiler/conformance fixtures, including malformed input. Tests compare diagnostic codes and UTF-16 spans plus top-level statement kinds and ranges. They run with ordinary `dotnet test`, without Node.js, network access or a compiler installation.

The oracle uses the compiler's parser diagnostics plus its variance order/duplicate grammar diagnostics, which TypeScript exposes through semantic diagnostics. It does not compare general type-checker diagnostics. This focused corpus and the portal regression check do not establish complete TypeScript 6 support; the imported grammar baseline remains July 2024 with the documented targeted fixes.

To refresh the oracle, run the .NET 10 file-based maintenance tool from the repository root with the matching official compiler. Node.js is needed only to invoke that compiler's API during regeneration. The tool has no NuGet dependencies and produces no JavaScript files:

```bash
dotnet run tools/UpdateTypeScriptCorpus.cs -- /path/to/typescript/lib/typescript.js
dotnet run tools/UpdateTypeScriptCorpus.cs -- /path/to/typescript/lib/typescript.js --check
dotnet test tests/SharpSense.TypeScript.Tests -c Release
```

An optional second path supplies an offline checkout of the pinned upstream revision, avoiding downloads. The tool records each upstream file's SHA-256, splits `@filename` fixtures into source units and checks the compiler version. `--check` verifies reproducibility without writing data.

For each parser change, run the corpus and extraction tests. When updating the supported syntax baseline, pin the new compiler version and source revision together, add fixtures for its new grammar, regenerate and review every changed diagnostic/range, and resolve differences before accepting the baseline. Do not regenerate expected results from the SharpSense parser. Keep syntax upgrades distinguishable from API and indexing changes, and validate the portal graph before release.

## Local validation

The October 2026 improvements were checked with a Release build and the backend test suite, including installed-package and watch failure/recovery tests. The portal regression corpus completed with zero diagnostics, retaining all 60,638 code-node identities and 356,514 persisted dependency edges. One type/value collision now identifies the value declaration consistently; its kind, hash and location changed as expected.

The isolated macOS ARM64 CLI runs reused the existing index and disabled embeddings:

| Measurement | Before improvements | After improvements |
| --- | ---: | ---: |
| Elapsed time | 177.94 seconds | 98.34 seconds |
| Peak resident memory | 1.12 GB | 1.33 GB |

These are local observations, not a cross-platform benchmark. Runtime improved by about 45%, while peak resident memory increased by about 19%. Source-line slices reduced the intermediate implementation's peak from 1.46 GB. The existing CI workflow remains the release validation path; these local runs do not establish results on other operating systems.
