using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.DependencyGraph.GetDependencyGraphEdges;
using SharpSense.Application.DependencyGraph.GetDependencyGraphEdges.Models;
using SharpSense.Application.DependencyGraph.GetDependencyGraphNodes;
using SharpSense.Application.DependencyGraph.GetDependencyGraphNodes.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.DependencyGraph;

public static class DependencyGraphServiceCollectionExtensions
{
    public static IServiceCollection AddDependencyGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IQueryHandler<GetDependencyGraphNodesQuery, IAsyncEnumerable<GraphNode>>, GetDependencyGraphNodesQueryHandler>();
        services.TryAddTransient<IQueryHandler<GetDependencyGraphEdgesQuery, IAsyncEnumerable<GraphEdge>>, GetDependencyGraphEdgesQueryHandler>();
        return services;
    }
}
