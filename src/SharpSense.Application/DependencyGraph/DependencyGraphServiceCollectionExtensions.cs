using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.DependencyGraph.GetGraphPages;
using SharpSense.Application.DependencyGraph.GetGraphPages.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.DependencyGraph;

public static class DependencyGraphServiceCollectionExtensions
{
    public static IServiceCollection AddDependencyGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IQueryHandler<GetGraphNodesPageQuery, GraphNodesPage>, GetGraphNodesPageQueryHandler>();
        services.TryAddTransient<IQueryHandler<GetGraphEdgesPageQuery, GraphEdgesPage>, GetGraphEdgesPageQueryHandler>();
        services
            .TryAddTransient<IQueryHandler<GetGraphNodeConnectionsQuery, GraphNodeConnectionsPage>, GetGraphNodeConnectionsQueryHandler>();

        return services;
    }
}
