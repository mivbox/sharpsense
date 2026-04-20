using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using SharpSense.Application.Features.ImpactAnalysis.Contracts;
using SharpSense.Application.Features.ImpactAnalysis.ImpactAnalysis;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Features.ImpactAnalysis;

public static class ImpactAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddImpactAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult>, ImpactAnalysisQueryHandler>();
        return services;
    }
}
