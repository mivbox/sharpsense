# TypeScript extraction

TypeScript and TSX sources use native Tree-sitter grammars. Select a tsconfig file or directory in a [workspace](../cli/workspace-command.md):

```bash
sharpsense workspace create frontend --repo-root /path/to/repository \
  --typescript frontend/tsconfig.json --markdown 'docs/frontend/**/*.md'
sharpsense analyze --workspace frontend
```

## Discovery and relationships

The extractor supports `.ts` and `.tsx` source files. It uses supported tsconfig membership, extended configs, project references, and path aliases, and follows repository-local imports and re-exports. Shared imported files can therefore sit outside the initially selected folder. Dependency and generated-output directories are excluded.

Tree-sitter parses declarations and expressions to build nodes and syntax-based relationships, including imports, calls where resolvable, type relationships, and TSX-specific references. Selected TypeScript sources are merged with C# and Markdown results before a single workspace graph commit.

This is not the TypeScript compiler's type checker. Dynamic values, complex type inference, overload resolution, runtime dependency injection, and inferred cross-language HTTP links are not guaranteed to resolve. JavaScript and `.mts`/`.cts` are not currently included by source discovery.

## Watching

Changes to source, imports, aliases, and configuration can affect consumers throughout the selected graph. Watch re-evaluates the source plan and replaces the complete workspace snapshot after successful extraction. Missing required source folders or configuration failures do not silently replace a valid graph with an empty one.

Use [doctor](../cli/doctor-command.md) to check native TypeScript/TSX parser availability and recorded indexing diagnostics. See [source discovery](../architecture/file-discovery.md) and [watch reconciliation](../architecture/incremental-watch.md).
