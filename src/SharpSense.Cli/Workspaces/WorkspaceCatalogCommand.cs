using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Workspaces;

internal abstract class WorkspaceCatalogCommand<TSettings> : AbstractAsyncCommand<TSettings>
    where TSettings : CliSettings
{
    protected override void Configure(TSettings settings, IServiceCollection services)
    {
        services.AddFileSystem();
        services.TryAddSingleton<WorkspaceSourceDiscovery>();
        services.TryAddSingleton<IWorkspaceInteractions, SpectreWorkspaceInteractions>();
        services.TryAddSingleton<WorkspaceSetup>();
        services.AddWorkspaceCatalog();
    }
}
