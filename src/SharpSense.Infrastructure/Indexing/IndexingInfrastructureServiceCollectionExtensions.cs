using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Features.Indexing.Infrastructure;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing.Markdown;

namespace SharpSense.Infrastructure.Indexing;

public static class IndexingInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddIndexingInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMsBuildWorkspaceFactory, MsBuildWorkspaceFactory>();
        services.AddSingleton<IRoslynSolutionAnalysisEngine, RoslynSolutionAnalysisEngine>();
        services.TryAddSingleton<MarkdownIndexer>();
        services.TryAddScoped<DocumentDiscoverer>();
        services.AddTransient<ILanguageExtractor, CSharpLanguageExtractor>();
        services.AddTransient<ILanguageExtractor, MarkdownDocumentExtractor>();
        services.TryAddScoped<IKnowledgeGraphIndexing, KnowledgeGraphIndexing>();

        return services;
    }
}
