using SharpSense.Application.DependencyGraph.Models;

namespace SharpSense.Application.DependencyGraph.Abstractions;

/// <summary>
/// Reads dependency-graph projections from the persisted knowledge graph so the UI can hydrate selected workspace scope
/// incrementally, loading nodes before edges while preserving the ghost-node boundary context needed to explain
/// cross-scope dependencies.
/// </summary>
public interface IDependencyGraphRepository
{
    /// <summary>
    /// Streams the graph nodes owned by the supplied workspace directories plus any one-hop external ghost nodes needed
    /// to explain cross-scope dependencies before the UI opts into edge hydration.
    /// </summary>
    /// <param name="directoryIds">Workspace directory ids selected in the explorer.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    IAsyncEnumerable<GraphNode> GetGraphNodes(
        IReadOnlyList<int> directoryIds,
        CancellationToken ct);

    /// <summary>
    /// Streams the internal and one-hop boundary edges for the supplied workspace directories after the UI has already
    /// loaded the corresponding node scope.
    /// </summary>
    /// <param name="directoryIds">Workspace directory ids selected in the explorer.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    IAsyncEnumerable<GraphEdge> GetGraphEdges(
        IReadOnlyList<int> directoryIds,
        CancellationToken ct);

}
