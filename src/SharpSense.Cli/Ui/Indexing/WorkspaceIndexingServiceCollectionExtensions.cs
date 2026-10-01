using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SharpSense.Cli.Ui.Indexing;

internal static class WorkspaceIndexingServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaceIndexing(this IServiceCollection services)
    {
        services.AddSingleton<WorkspaceIndexingCoordinator>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<WorkspaceIndexingCoordinator>());

        return services;
    }
}
