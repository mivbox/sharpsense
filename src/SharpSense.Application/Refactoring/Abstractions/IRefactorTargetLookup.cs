using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Application.Refactoring.Abstractions;

/// <summary>
/// Resolves the persisted file location, line metadata, and document kind for a stored code-node id so the shared
/// semantic-rename boundary can stay independent from EF Core query details.
/// </summary>
public interface IRefactorTargetLookup
{
    /// <summary>
    /// Looks up the persisted target metadata required to rename the node represented by the supplied id.
    /// </summary>
    /// <param name="nodeId">The persisted integer node id.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current lookup.</param>
    /// <returns>The target location when the node exists; otherwise <c>null</c>.</returns>
    Task<NodeRefactorTarget?> GetTarget(
        int nodeId,
        CancellationToken ct);
}
