using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Features.Indexing.Infrastructure;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;

namespace SharpSense.Infrastructure.Indexing;

public static class IndexingInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddIndexingInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMsBuildWorkspaceFactory, MsBuildWorkspaceFactory>();
        services.AddSingleton<IRoslynSolutionAnalysisEngine, RoslynSolutionAnalysisEngine>();
        services.TryAddScoped<IKnowledgeGraphIndexing, KnowledgeGraphIndexing>();

        return services;
    }
}
