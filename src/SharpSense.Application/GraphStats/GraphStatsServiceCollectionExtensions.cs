using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.GraphStats.GetGraphStats;
using SharpSense.Application.GraphStats.GetGraphStats.Models;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.GraphStats;

public static class GraphStatsServiceCollectionExtensions
{
    public static IServiceCollection AddGraphStats(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IQueryHandler<GetGraphStatsQuery, GraphStatsSnapshot>, GetGraphStatsQueryHandler>();

        return services;
    }
}
