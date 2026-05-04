using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Application.Refactoring.Abstractions;

/// <summary>
/// Owns the document-kind-specific mechanics required to rename a persisted node target without leaking Roslyn or
/// file-system concerns into the shared Application orchestration.
/// </summary>
public interface IWorkspaceRenamer
{
    /// <summary>
    /// Renames the supplied target using the correct strategy for its persisted document kind and reports all modified
    /// repository-relative files when the operation succeeds.
    /// </summary>
    /// <param name="target">The persisted target metadata for the symbol to rename.</param>
    /// <param name="newName">The new identifier name for the target.</param>
    /// <param name="targetPath">An optional explicit workspace target path for strategies that require a Roslyn workspace. When omitted, the implementation resolves the target from persisted metadata or the current repository context.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current refactor operation.</param>
    Task<RefactorResult> RenameSymbol(
        NodeRefactorTarget target,
        string newName,
        string? targetPath = null,
        CancellationToken ct = default);
}
