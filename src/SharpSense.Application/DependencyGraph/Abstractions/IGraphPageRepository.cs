using SharpSense.Application.DependencyGraph.Models;

namespace SharpSense.Application.DependencyGraph.Abstractions;

public interface IGraphPageRepository
{
    Task<GraphNodesPage> GetNodesPage(GraphPageRequest request, CancellationToken ct);

    Task<GraphEdgesPage> GetEdgesPage(GraphPageRequest request, CancellationToken ct);

    Task<GraphNodeConnectionsPage> GetNodeConnections(GraphNodeConnectionsRequest request, CancellationToken ct);
}
