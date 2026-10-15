# Parser provenance

Imported from serenity-is/Serenity commit a2f5558039b2aa0feb24230f8c8a8a6bbb81885a, src/typescript.
The parser records Microsoft TypeScript baseline 9757109cafcb771a35ad9fe09855373cdd82005a (2024-07-29).

Copyright Microsoft Corporation and the Serenity project contributors. The parser is distributed under the included Apache 2.0 license.

SharpSense changes: .NET 10 project and feature layout, namespaces and formatting, restricted implementation visibility, read-only syntax contracts and corrected optional children, owned parse results and diagnostics, bounded grammar recursion/source size/node work, scanner cancellation, iterative tree operations, diagnostic deduplication and child-error aggregation, composite scanner flags, import-type attribute trailing commas, variance order/duplicate diagnostics, identifier text reuse and extraction integration.

The differential test corpus separately pins Microsoft TypeScript 6.0.3 at 050880ce59e30b356b686bd3144efe24f875ebc8. This focused comparison does not advance the complete imported grammar baseline to TypeScript 6. See docs/wiki/extractors/typescript.md for the baseline update workflow.
