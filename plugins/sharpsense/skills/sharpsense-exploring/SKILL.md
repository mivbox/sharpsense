---
name: sharpsense-exploring
description: "Find unfamiliar implementations with SharpSense semantic search before guessing filenames or symbols. Use during debugging, feature work, refactoring, or review when the implementation location is unknown, and for code-relationship questions."
---

# Find and explore code with SharpSense

Use semantic search to locate relevant implementations from the behavior or
concept being investigated. This discovery workflow can be part of a larger
implementation, debugging, or review task; continue that task after locating
the relevant code.

## Choose the discovery method

When the implementation location is unknown, start with `semantic_search`
in the verified workspace. This includes requests described in terms of
business behavior, symptoms, architectural responsibilities, or desired
features.

Do not start implementation discovery with guessed filenames, symbols, or
directories, including `rg --files` globs. Use semantic search to get the
first implementation locations. If you need to find `AGENTS.md`, keep that
instruction lookup separate from implementation discovery.

When the relevant file or method location is already known, read it directly.
Use `rg` for exact strings, exhaustive occurrence checks, and source coverage
that the index cannot provide.

## Verify the workspace

Before semantic search or graph navigation, call `graph_stats({})` once
unless this session has already verified the current MCP workspace.
Check its root, language coverage, indexing outcome,
and diagnostics against the intended source. Reuse that verification while
the working context and server binding remain unchanged.

An index timestamp does not prove that the graph matches current source.
If the binding is wrong or the graph is unavailable, continue with source
tools and state the material limitation once. Do not create or reindex a
workspace merely to perform discovery.

## Search from the behavior

Use a concise phrase describing the behavior or responsibility:

- Tenant ingestion limits: `tenant ingestion concurrency`
- Work continuing after cancellation: `conversation cancellation`
- An existing upload implementation: `document upload`

Start with a focused query and a small result set:

`semantic_search({query: "<behavior or concept>", limit: 5})`

A known distinctive symbol is also a useful query. Search is plain text;
FTS operators and column selectors are not supported. Multiple terms can
broaden lexical matching, so avoid combining several unrelated concepts
or a list of guessed identifiers.

Inspect the returned paths, symbols, and source locations. Read the relevant
implementation before making a behavioral claim. If results are irrelevant,
refine the concept or use targeted source searches to resolve the gap.
Do not repeatedly issue equivalent queries.

## Follow unresolved relationships

Use `context({nodeId: 42})` when immediate callers, callees, or inheritance
would answer a remaining question.

Use `trace_node({nodeId: "42", direction: "caller"})` for upstream consumers,
or `direction: "callee"` for downstream dependencies. Trace the relevant
member when investigating behavior. Use `get_inheritors({nodeId: 42})` for
direct implementations or derived types.

Search does not require a subsequent context or trace call. Stop expanding
when the source evidence answers the question. Reuse established node IDs
only within the same workspace.

For relevant recorded rationale, request `edgeCategories: "All"` on context
or trace and fetch the surfaced notes with `get_memory` or `get_memories`.
Verify notes against current source. Discovery does not authorize memory writes.

## Report the evidence

Explain the behavior or complete the requested work using verified source
locations. Distinguish observed behavior from inference.

Missing edges, empty searches, truncated results, and syntax-based TypeScript
relationships do not establish that behavior or consumers are absent.
Static traces do not establish runtime execution order.

For CLI fallback, inspect command help and select the workspace explicitly;
caller tracing defaults differ between CLI and MCP.

Omit routine tool logs and empty report sections. Delegate only when
delegation is authorized and an independent investigation would help.
