using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing.CSharp;
using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Indexing.TypeScript;
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
        services.TryAddScoped<IWorkspaceLoader, WorkspaceLoader>();
        services.TryAddSingleton<ITargetAnalysisEngine, RoslynTargetAnalysisEngine>();
        services.TryAddSingleton<IMarkdownIndexer, MarkdownIndexer>();
        services.TryAddScoped<IWorkspaceWatcher, WorkspaceWatcher>();
        services.TryAddScoped<IWorkspaceFileDiscoverer, WorkspaceFileDiscoverer>();
        services.TryAddScoped<IIndexingWorkspacePaths, IndexingWorkspacePaths>();
        services.TryAddScoped<IWorkspaceChangeFilter, WorkspaceChangeFilter>();
        services.TryAddScoped<DocumentDiscoverer>();
        services.TryAddScoped<TsConfigResolver>();
        services.TryAddScoped<TypeScriptSourceDiscoverer>();
        services.TryAddScoped<ICSharpWorkspaceTargetResolver, CSharpWorkspaceTargetResolver>();
        services.AddTransient<ITypeScriptExtractionPass, CodeNodeExtractionPass>();
        services.AddTransient<ITypeScriptExtractionPass, ImportDependencyPass>();
        services.AddTransient<ITypeScriptExtractionPass, HttpEdgeExtractionPass>();
        services.AddTransient<ILanguageExtractor, CSharpLanguageExtractor>();
        services.AddTransient<ILanguageExtractor, MarkdownDocumentExtractor>();
        services.AddTransient<ILanguageExtractor, TypeScriptLanguageExtractor>();
        services.TryAddScoped<IKnowledgeGraphRepository, KnowledgeGraphRepository>();

        return services;
    }
}
