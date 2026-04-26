using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing.CSharp;
using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Indexing.Watching;

namespace SharpSense.Infrastructure.Indexing;

public static class IndexingInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddIndexingInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMsBuildWorkspaceFactory, MsBuildWorkspaceFactory>();
        services.AddSingleton<IRoslynTargetAnalysisEngine, RoslynTargetAnalysisEngine>();
        services.TryAddSingleton<MarkdownIndexer>();
        services.TryAddSingleton<IWorkspaceWatcher, WorkspaceWatcher>();
        services.TryAddSingleton<IWorkspaceFileDiscoverer, WorkspaceFileDiscoverer>();
        services.TryAddSingleton<IIndexingWorkspacePaths, IndexingWorkspacePaths>();
        services.TryAddScoped<DocumentDiscoverer>();
        services.AddTransient<ILanguageExtractor, CSharpLanguageExtractor>();
        services.AddTransient<ILanguageExtractor, MarkdownDocumentExtractor>();
        services.TryAddScoped<IKnowledgeGraphRepository, KnowledgeGraphRepository>();

        return services;
    }
}
