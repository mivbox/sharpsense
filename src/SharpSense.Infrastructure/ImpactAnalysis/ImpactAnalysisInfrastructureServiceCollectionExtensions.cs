using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.ImpactAnalysis.Abstractions;

namespace SharpSense.Infrastructure.ImpactAnalysis;

public static class ImpactAnalysisInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddImpactAnalysisInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IImpactAnalyzer, ImpactAnalyzer>();

        return services;
    }
}
