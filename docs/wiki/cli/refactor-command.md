---
title: "Refactor Command"
type: cli
tags: [spectre, roslyn, incremental-watch, implemented]
created: 2026-05-02
updated: 2026-05-02
confidence: high
---

## Command

`sharp-sense refactor --node-id <node-id>` replaces the persisted source span for a code node and writes the updated file through Roslyn. It is the CLI counterpart to MCP `refactor_node` on [[cli/mcp-command]], and both routes share the same injected `INodeRefactorer` boundary instead of a CQRS handler. The command stops at the disk write; asynchronous index and vector refresh still flow through [[architecture/incremental-watch]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `NodeId` | `--node-id <NODE_ID>` | Persisted integer code-node id whose source span should be replaced. |
| `FilePath` | `--file <path>` | Optional `.cs` or `.txt` file containing the replacement source. |
| `TargetPath` | `--target <path>` | Optional explicit `.sln` or `.csproj` target. |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace that owns the SQLite index and default target discovery root. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose command-host logging. |

If `--file` is omitted, `RefactorCommand` reads the replacement source from `Console.In`. Relative `--file` paths are resolved from the resolved repository root so CLI and MCP use the same workspace context.

## Execution Flow

1. `Program.CommandApp.cs` routes `refactor` to `RefactorCommand`.
2. Spectre validates that `--node-id` is positive and that `--file`, when present, uses `.cs` or `.txt`.
3. `AbstractAsyncCommand<TSettings>` builds the host using the shared rules in [[architecture/host-composition]].
4. `Configure()` resolves the repository root and registers `AddRefactoring()`, `AddRefactoringInfrastructure()`, `AddIndexingInfrastructure()`, and `AddPersistence()`.
5. `Execute()` reads the replacement source from the file path or stdin, then resolves `INodeRefactorer` directly so CLI and MCP share the exact same Application orchestration.
6. `NodeRefactorer` loads the persisted file path and line span through `IRefactorTargetLookup`.
7. `WorkspaceTargetResolver` picks an explicit Target when `--target` is supplied, otherwise it prefers a single `.sln` in the repository root and then a single `.csproj`, failing fast when discovery is ambiguous.
8. `RoslynWorkspaceRefactorer` converts the 1-based persisted line span to a Roslyn `TextSpan`, trims trailing line endings from the replacement source, and delegates the locked workspace mutation to `WorkspaceLoader.ChangeDocumentText()`.
9. `WorkspaceLoader.ChangeDocumentText()` applies the Roslyn change while holding the cached workspace gate so concurrent refactors cannot diff against a stale `CurrentSolution`.
10. `TokenObjectNotation.SerializeRefactorResult()` prints compact TOON success or failure output directly to the terminal.
11. The already-running watch/index loop from [[architecture/incremental-watch]] observes the modified file and refreshes the persisted graph/vector store asynchronously after the command returns.
