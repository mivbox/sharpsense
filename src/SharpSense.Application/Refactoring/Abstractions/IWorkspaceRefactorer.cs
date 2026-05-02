using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Application.Refactoring.Abstractions;

/// <summary>
/// Owns the workspace-specific mechanics required to replace a persisted node span inside the active target without
/// leaking Roslyn or file-system concerns into the shared Application orchestration.
/// </summary>
public interface IWorkspaceRefactorer
{
    /// <summary>
    /// Replaces the supplied target span inside the resolved workspace target and writes the updated source to disk.
    /// </summary>
    /// <param name="target">The persisted file path and line span to replace.</param>
    /// <param name="newSourceCode">The raw source text that should replace the current span.</param>
    /// <param name="targetPath">An optional explicit workspace target path. When omitted, the implementation resolves the target from the current repository context.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current refactor operation.</param>
    Task<RefactorResult> RefactorNode(
        RefactorTarget target,
        string newSourceCode,
        string? targetPath = null,
        CancellationToken ct = default);
}
