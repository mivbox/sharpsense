using SharpSense.Application.DependencyGraph.Models;

namespace SharpSense.Application.DependencyGraph.Abstractions;

/// <summary>
/// Reads dependency-graph projections from the persisted knowledge graph so the UI can request only the selected
/// analyzed scope and any explicit boundary nodes required to explain cross-scope dependencies.
/// </summary>
public interface IDependencyGraphRepository
{
    /// <summary>
    /// Returns the graph owned by the supplied selected directories plus optional boundary nodes and edges that cross from
    /// selected scope to external scope.
    /// </summary>
    /// <param name="directoryIds">Workspace directory ids selected in the explorer.</param>
    /// <param name="includeBoundaryNodes"><c>true</c> to include one-hop external endpoints as ghost nodes.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<GraphResult> GetGraph(
        IReadOnlyList<int> directoryIds,
        bool includeBoundaryNodes,
        CancellationToken ct);
}
