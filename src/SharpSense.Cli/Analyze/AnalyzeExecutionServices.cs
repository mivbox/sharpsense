using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Shared;
using SharpSense.Cli.Workspaces;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Cli.Analyze;

internal static class AnalyzeExecutionServices
{
    public static IServiceCollection AddAnalyzeExecution(this IServiceCollection services)
    {
        services.AddWorkspaceExecutionServices();
        services.TryAddSingleton<WorkspaceSourceDiscovery>();
        services.TryAddSingleton<IWorkspaceInteractions, SpectreWorkspaceInteractions>();
        services.TryAddSingleton<WorkspaceSetup>();
        services.TryAddSingleton<WorkspaceAnalysisRunner>();
        services.TryAddScoped<IAnalysisDatabaseInitializer, AnalysisDatabaseInitializer>();
        services.AddIndexing();
        services.AddEmbeddingsInfrastructure();
        services.AddIndexingInfrastructure();
        services.AddIndexRunRecording();
        return services;
    }
}

internal interface IAnalysisDatabaseInitializer
{
    Task Initialize(CancellationToken ct);
}

internal sealed class AnalysisDatabaseInitializer(WorkspaceDatabaseInitializer initializer) : IAnalysisDatabaseInitializer
{
    public Task Initialize(CancellationToken ct) => initializer.InitializeAsync(ct);
}
