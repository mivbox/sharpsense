using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.DependencyGraph.GetGraphPages;

public sealed class GetGraphEdgesPageQueryHandler(IGraphPageRepository repository)
    : IQueryHandler<GetGraphEdgesPageQuery, GraphEdgesPage>
{
    public Task<GraphEdgesPage> Handle(GetGraphEdgesPageQuery query, CancellationToken ct)
        => repository.GetEdgesPage(query.Page, ct);
}
