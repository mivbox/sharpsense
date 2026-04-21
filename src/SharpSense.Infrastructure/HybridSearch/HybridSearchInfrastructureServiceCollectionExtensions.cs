using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Features.HybridSearch.Infrastructure;

namespace SharpSense.Infrastructure.HybridSearch;

public static class HybridSearchInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddHybridSearchInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IHybridSearcher, HybridSearcher>();
        return services;
    }
}
