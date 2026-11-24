# FTS query patterns and shell quoting

## Useful FTS expressions

| Pattern | Meaning | Example |
| --- | --- | --- |
| `a OR b` | Either term appears | `error OR warning OR failed` |
| `a AND b` | Both terms appear on the line | `CS8602 AND SharpSense` |
| `"exact phrase"` | Exact token sequence | `"Build FAILED"` |
| `a NOT b` | Include `a`, exclude `b` | `warning NOT CS1591` |

Do not pass prose prompts such as `"summarize what failed"` — FTS treats each word as a literal token query.

## Explicit shell quoting

Because `ctx_execute` launches the executable directly without a shell:

```json
{"command": "dotnet test \"My App.sln\" --no-build", "query": "Failed OR Exception"}
{"command": "bash -c \"dotnet build SharpSense.sln --no-restore && dotnet test SharpSense.sln --no-build\"", "query": "error OR Failed OR Passed"}
```

## Reading result fields

- `status: error` means the command failed to start or validate (protocol `isError: true`).
- `status: failed` with a numeric `exit_code` means the child process ran and exited nonzero.
- `truncated: true` means matching output exceeded the capture or block budget; inspect a log file or narrow the command target rather than guessing missing lines.
