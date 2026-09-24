# Analyze and watch

Analyze builds one graph from every source selected in a [workspace](workspace-command.md):

```bash
sharpsense analyze --workspace product
sharpsense analyze --workspace product --watch
```

The command takes no positional solution or directory. Configure C# projects/solutions, TypeScript projects/folders, and Markdown files/globs in the workspace first.

| Option | Behavior |
| --- | --- |
| `--workspace <name-or-id>` | Select a registered workspace; otherwise use the unique repository match, or offer an interactive picker/setup. |
| `--watch` | After the initial index, process relevant filesystem changes until stopped. |
| `--no-embeddings` | Skip generating embeddings; structural graph queries remain available. |
| `--no-cache` | Regenerate embeddings instead of reusing matching persisted vectors. |
| `--repo-root <path>` | Use this directory when resolving an implicit workspace. |
| `-v` / `--verbose` | Enable verbose logging. |

Interactive output uses an injected Spectre console to show elapsed time, named phases, and concurrent language/source activity. Embedding and source counts appear when known; there is no estimated overall percentage. Completion summaries report committed node/edge/document counts and available source/embedding reuse. Bounded diagnostics remain visible. Redirected output uses concise milestones and never prompts.

Watch output distinguishes initial reconciliation, idle watching, updates, full recovery, and stopping. Ctrl+C cancels work gracefully and releases the workspace lease. The manager and explicit `analyze` command use the same runner.

Extraction combines selected sources before committing a complete graph snapshot. A required source failure preserves the previous graph. Unchanged nodes can retain their identities and embeddings; newly missing nodes are removed, including memories attached to removed nodes.

C# warning-as-error build policies do not promote warnings during SharpSense's project load. MSBuild/NuGet warnings remain visible in index diagnostics. Actual load failures still abort the index; see [C# errors and warnings](../extractors/csharp.md#errors-and-warnings).

Watch mode filters unrelated changes, batches relevant ones, and reconciles the complete selected graph. C# can reuse a loaded Roslyn workspace for suitable edits, but named-workspace persistence is not limited to changed documents. See [watch reconciliation](../architecture/incremental-watch.md).

One process holds an indexing lease for the entire analysis/watch session, including initialization. A second indexer or source edit for the same workspace fails with an actionable busy message. Stop watch before editing the definition, then restart analysis; workspace configuration is not live-reloaded.

Use [doctor](doctor-command.md) for coverage and recorded failures. Analyze success alone does not guarantee every language relationship is resolved; see the [C#](../extractors/csharp.md) and [TypeScript](../extractors/typescript.md) extractor notes.
