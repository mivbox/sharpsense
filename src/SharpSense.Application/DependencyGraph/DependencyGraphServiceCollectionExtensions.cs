using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.DependencyGraph.GetDependencyGraph;
using SharpSense.Application.DependencyGraph.GetDependencyGraph.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.DependencyGraph;

public static class DependencyGraphServiceCollectionExtensions
{
    public static IServiceCollection AddDependencyGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IQueryHandler<GetDependencyGraphQuery, GraphResult>, GetDependencyGraphQueryHandler>();
        return services;
    }
}
