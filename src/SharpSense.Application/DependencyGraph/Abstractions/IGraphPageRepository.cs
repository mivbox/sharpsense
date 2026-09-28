using SharpSense.Application.DependencyGraph.Models;

namespace SharpSense.Application.DependencyGraph.Abstractions;

/// <summary>Reads bounded graph pages for the selected workspace and graph revision.</summary>
public interface IGraphPageRepository
{
    /// <summary>Returns the next filtered node page, rejecting a stale graph revision.</summary>
    Task<GraphNodesPage> GetNodesPage(GraphPageRequest request, CancellationToken ct);

    /// <summary>Returns the next filtered edge page, rejecting a stale graph revision.</summary>
    Task<GraphEdgesPage> GetEdgesPage(GraphPageRequest request, CancellationToken ct);

    /// <summary>Returns a bounded page of relationships for one node.</summary>
    Task<GraphNodeConnectionsPage> GetNodeConnections(GraphNodeConnectionsRequest request, CancellationToken ct);
}
