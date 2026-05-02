using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Application.Refactoring.Abstractions;

/// <summary>
/// Resolves the persisted file location and line span for a stored code-node id so the shared refactor boundary can stay
/// independent from EF Core query details.
/// </summary>
public interface IRefactorTargetLookup
{
    /// <summary>
    /// Looks up the repository-relative file path and persisted line span for a stored node id.
    /// </summary>
    /// <param name="nodeId">The persisted integer node id.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current lookup.</param>
    /// <returns>The target location when the node exists; otherwise <c>null</c>.</returns>
    Task<RefactorTarget?> GetTarget(
        int nodeId,
        CancellationToken ct);
}
