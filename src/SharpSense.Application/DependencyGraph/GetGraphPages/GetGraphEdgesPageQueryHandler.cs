using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.GetGraphPages.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.DependencyGraph.GetGraphPages;

internal sealed class GetGraphEdgesPageQueryHandler(IGraphPageRepository repository)
    : IQueryHandler<GetGraphEdgesPageQuery, GraphEdgesPage>
{
    public Task<GraphEdgesPage> Handle(GetGraphEdgesPageQuery query, CancellationToken ct)
        => repository.GetEdgesPage(query.Page, ct);
}
