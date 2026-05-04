---
title: "Refactor Command"
type: cli
tags: [spectre, roslyn, incremental-watch, implemented]
created: 2026-05-02
updated: 2026-05-02
confidence: high
---

## Command

`sharp-sense refactor --node-id <node-id> --new-name <new-name>` semantically renames the declared symbol represented by a persisted node id. It is the CLI counterpart to MCP `refactor_symbol` on [[cli/mcp-command]], and both routes share the same injected `IRefactorSymbolService` boundary instead of a CQRS handler. The command stops after writing the rename to disk; asynchronous index and vector refresh still flow through [[architecture/incremental-watch]].

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `NodeId` | `--node-id <node-id>` | Persisted integer code-node id whose declared symbol should be renamed. |
| `NewName` | `--new-name <new-name>` | New identifier only. Mirrors MCP `newName`; do not pass a signature or code block. |
| `TargetPath` | `--target <path>` | Optional explicit `.sln` or `.csproj` override when the selected strategy uses a Roslyn workspace and you do not want default workspace resolution. |
| `RepositoryRoot` | `--repo-root <path>` | Resolves the repository workspace that owns the SQLite index and default workspace discovery root. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose command-host logging. |

`RefactorSymbolCommand` uses explicit options so the CLI surface stays unambiguous about semantic rename intent.

## Execution Flow

1. `Program.CommandApp.cs` routes `refactor` to `RefactorSymbolCommand`.
2. Spectre validates that `--node-id` is positive and `--new-name` is non-empty.
3. `AbstractAsyncCommand<TSettings>` builds the host using the shared rules in [[architecture/host-composition]].
4. `Configure()` resolves the repository root and registers `AddRefactoring()`, `AddRefactoringInfrastructure()`, `AddIndexingInfrastructure()`, and `AddPersistence()`.
5. `Execute()` forwards `nodeId`, `newName`, and the optional `targetPath` to `IRefactorSymbolService` so CLI and MCP share the same Application orchestration.
6. `RefactorSymbolService` validates inputs, resolves persisted target metadata through `IRefactorTargetLookup`, and delegates the rename to `IWorkspaceRenamer`.
7. `RefactorTargetLookup` loads the persisted relative file path, start line, document kind, and owning project path through `CodeNode.ProjectNodeId -> ProjectNode.ProjectDocumentId -> Document.RelativePath`.
8. `WorkspaceRenamer` selects the concrete `IRenameStrategy` from the persisted document kind.
9. `RoslynSymbolRenameStrategy` handles `Source` nodes: it resolves a workspace target from `--target`, repo-root discovery, or the persisted owning project; locates the declaration from the persisted start line; resolves the declared symbol from Roslyn; runs `Renamer.RenameSymbolAsync(...)`; computes modified files from the old/new solution diff; and applies the renamed solution through `workspace.TryApplyChanges(...)`.
10. `MarkdownRenameStrategy` handles `Markdown` nodes by rewriting the first Markdown heading inside the persisted span and returning the modified Markdown file path only. It does not attempt cross-file link updates.
11. `TokenObjectNotation.SerializeRefactorResult()` prints compact TOON success or failure output directly to the terminal.
12. The already-running watch/index loop from [[architecture/incremental-watch]] observes the modified files and refreshes the persisted graph/vector store asynchronously after the command returns.
