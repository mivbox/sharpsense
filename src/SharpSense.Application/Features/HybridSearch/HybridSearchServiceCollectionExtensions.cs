using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using SharpSense.Application.Features.HybridSearch.Contracts;
using SharpSense.Application.Features.HybridSearch.HybridSearch;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Features.HybridSearch;

public static class HybridSearchServiceCollectionExtensions
{
    public static IServiceCollection AddHybridSearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IQueryHandler<HybridSearchQuery, HybridSearchResult>, HybridSearchQueryHandler>();
        return services;
    }
}
