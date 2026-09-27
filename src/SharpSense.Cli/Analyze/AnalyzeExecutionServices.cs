using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Shared;
using SharpSense.Cli.Workspaces;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.Indexing;

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
        services.AddIndexing();
        services.AddEmbeddingsInfrastructure();
        services.AddIndexingInfrastructure();
        services.AddIndexRunRecording();

        return services;
    }
}
