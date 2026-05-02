using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Application.Refactoring.Abstractions;

/// <summary>
/// Orchestrates persisted-node lookups and workspace-backed source replacement so every write-capable interface in the
/// product mutates code through one shared Application-layer boundary.
/// </summary>
public interface INodeRefactorer
{
    /// <summary>
    /// Replaces the full source span for the persisted node id with the supplied source text and reports the modified
    /// repository-relative files when the write succeeds.
    /// </summary>
    /// <param name="nodeId">The persisted integer node id to replace.</param>
    /// <param name="newSourceCode">The raw source text that should replace the current node span.</param>
    /// <param name="targetPath">An optional explicit workspace target path. When omitted, the implementation resolves the target from the current repository context.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current refactor operation.</param>
    Task<RefactorResult> RefactorNode(
        int nodeId,
        string newSourceCode,
        string? targetPath = null,
        CancellationToken ct = default);
}
