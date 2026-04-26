using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.ImpactAnalysis;

public static class ImpactAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddImpactAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult>, ImpactAnalysisQueryHandler>();
        return services;
    }
}
