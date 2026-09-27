using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.GraphStats.Abstractions;

namespace SharpSense.Infrastructure.GraphStats;

public static class GraphStatsInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddGraphStatsInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IGraphStatsReader, GraphStatsReader>();

        return services;
    }

    public static IServiceCollection AddIndexRunRecording(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IIndexRunStore, IndexRunStore>();

        return services;
    }
}
