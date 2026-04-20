using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using SharpSense.Application.Features.DependencyGraph.Contracts;
using SharpSense.Application.Features.DependencyGraph.GetDependencyGraph;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Features.DependencyGraph;

public static class DependencyGraphServiceCollectionExtensions
{
    public static IServiceCollection AddDependencyGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IQueryHandler<GetDependencyGraphQuery, GraphResult>, GetDependencyGraphQueryHandler>();
        return services;
    }
}
