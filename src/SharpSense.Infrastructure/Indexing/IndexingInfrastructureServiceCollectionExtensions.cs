using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing.CSharp;
using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Indexing.Watching;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing;

public static class IndexingInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddIndexingInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFileSystem();
        services.AddSingleton<IMsBuildWorkspaceFactory, MsBuildWorkspaceFactory>();
        services.TryAddSingleton<NodeExtractor>();
        services.TryAddSingleton<EdgeExtractor>();
        services.TryAddSingleton<IWorkspaceLoader, WorkspaceLoader>();
        services.TryAddSingleton<ITargetAnalysisEngine>(serviceProvider => new RoslynTargetAnalysisEngine(
            serviceProvider.GetRequiredService<NodeExtractor>(),
            serviceProvider.GetRequiredService<EdgeExtractor>(),
            serviceProvider.GetRequiredService<System.IO.Abstractions.IFileSystem>()));
        services.TryAddSingleton<IMarkdownIndexer, MarkdownIndexer>();
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
