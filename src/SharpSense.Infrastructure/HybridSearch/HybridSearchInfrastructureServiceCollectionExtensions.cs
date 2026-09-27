using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.HybridSearch.Abstractions;

namespace SharpSense.Infrastructure.HybridSearch;

public static class HybridSearchInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddHybridSearchInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IKeywordCandidateProvider, SqliteKeywordCandidateProvider>();
        services.AddScoped<IHybridSearcher, HybridSearcher>();

        return services;
    }
}
