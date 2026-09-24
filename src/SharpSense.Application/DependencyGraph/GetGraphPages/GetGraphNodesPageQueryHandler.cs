using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.DependencyGraph.GetGraphPages;

public sealed class GetGraphNodesPageQueryHandler(IGraphPageRepository repository)
    : IQueryHandler<GetGraphNodesPageQuery, GraphNodesPage>
{
    public Task<GraphNodesPage> Handle(GetGraphNodesPageQuery query, CancellationToken ct)
        => repository.GetNodesPage(query.Page, ct);
}
