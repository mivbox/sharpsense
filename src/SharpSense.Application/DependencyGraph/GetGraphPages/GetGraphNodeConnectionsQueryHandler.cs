using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.GetGraphPages.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.DependencyGraph.GetGraphPages;

internal sealed class GetGraphNodeConnectionsQueryHandler(IGraphPageRepository repository)
    : IQueryHandler<GetGraphNodeConnectionsQuery, GraphNodeConnectionsPage>
{
    public Task<GraphNodeConnectionsPage> Handle(GetGraphNodeConnectionsQuery query, CancellationToken ct)
        => repository.GetNodeConnections(query.Page, ct);
}
