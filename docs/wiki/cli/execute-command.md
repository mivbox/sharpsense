# Command execution

Execute runs a local command in the selected workspace's repository and reduces its output:

```bash
sharpsense execute "dotnet build" --workspace product --query "error OR warning"
sharpsense execute "git status --short" --workspace product --toon
```

Quote the full command string. The runner starts an executable directly, closes its stdin, and captures stdout/stderr. Shell pipes, redirection, and command substitution are not interpreted. The command retains the SharpSense process's normal filesystem permissions.

`--query` is an optional SQLite FTS query. Matching lines are expanded into bounded context windows and overlapping windows are merged. Captured-line and returned-character limits prevent unlimited output growth. Missing or unmatched queries produce a compact summary rather than the complete transcript.

JSON is the default; `--toon` returns compact metadata and selected blocks. Check the returned exit code and truncation information when diagnosing a command.

The MCP `ctx_execute` tool shares the [windowed execution pipeline](../architecture/windowed-execution-pipeline.md). Output indexing uses an independent in-memory SQLite store; transcripts are not persisted in the workspace graph.
