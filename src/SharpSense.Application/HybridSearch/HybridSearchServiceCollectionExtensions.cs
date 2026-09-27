using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.HybridSearch.HybridSearch;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.HybridSearch;

public static class HybridSearchServiceCollectionExtensions
{
    public static IServiceCollection AddHybridSearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IQueryHandler<HybridSearchQuery, HybridSearchResult>, HybridSearchQueryHandler>();

        return services;
    }
}
