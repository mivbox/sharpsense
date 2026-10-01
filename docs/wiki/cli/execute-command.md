# Command execution

Execute runs a local command in the selected workspace's repository and reduces its output:

```bash
sharpsense execute "dotnet build" --workspace product --query "error OR warning"
sharpsense execute "git status --short" --workspace product --toon
```

Quote the full command string. The runner starts an executable directly, closes its stdin, and captures stdout/stderr. Shell pipes, redirection, and command substitution are not interpreted. The command retains the SharpSense process's normal filesystem permissions.

`--query` is an optional SQLite FTS query. Matching lines are expanded into bounded context windows and overlapping windows are merged. Capture retains at most 5,000 lines and 1 MiB of UTF-8 line text across stdout and stderr combined; line terminators do not count toward the byte budget. Oversized lines retain a complete Unicode prefix. Remaining output is drained, the observed line count continues increasing, and the result reports truncation. Returned blocks also have a separate character limit. Missing or unmatched queries produce a compact summary rather than the complete transcript.

JSON is the default; `--toon` returns compact metadata and selected blocks. Check the returned exit code and truncation information when diagnosing a command. Cancelling `execute` returns exit code 130 after stopping the child process; an interrupted command is not reported as successful.

The MCP `ctx_execute` tool shares the [windowed execution pipeline](../architecture/windowed-execution-pipeline.md). Output indexing uses an independent in-memory SQLite store; transcripts are not persisted in the workspace graph.
