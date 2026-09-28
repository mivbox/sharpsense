# Doctor and graph statistics

`doctor` reports the selected workspace's configuration, prerequisites, graph coverage, and recorded indexing outcomes:

```bash
sharpsense doctor --workspace product
sharpsense doctor --workspace product --json
```

The command checks the workspace definition, .NET SDK, TypeScript/TSX parser availability, and embedding assets. It reports database state, node/file/edge/memory counts, embedding coverage, the last successful index, and the last attempt's diagnostics. Embedding asset checks do not prove inference succeeds.

Doctor does not create, migrate, or reset a workspace database. A workspace that has not been analyzed can be inspected before initialization. Errors produce a nonzero exit code; warnings are reported without treating them as fatal.

The MCP `graph_stats` tool exposes graph statistics and recorded indexing diagnostics without requiring a node ID. The [UI](ui-command.md) presents the same index information. These surfaces describe the stored index and its recent runs, not live watcher health.

If a source or prerequisite is missing, correct it and rerun [analyze](analyze-command.md). If an index is incompatible, preserve it and follow the diagnostic rather than deleting data automatically.
