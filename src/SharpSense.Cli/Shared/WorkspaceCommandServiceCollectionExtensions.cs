using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Shared.Options;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Shared;

internal static class WorkspaceCommandServiceCollectionExtensions
{
    public static IServiceCollection AddSelectedWorkspace(
        this IServiceCollection services,
        GlobalSettings settings,
        bool discoverFromDirectory = false)
    {
        if (discoverFromDirectory && settings.Workspace is null)
        {
            var workingDirectory = CommandPathResolver.ResolveWorkspaceRoot(settings.WorkspaceRoot);
            services.AddWorkspaceCatalog();
            services
                .TryAddSingleton(provider =>
                provider.GetRequiredService<IWorkspaceCatalog>()
                    .ResolveFromDirectory(workingDirectory));
        }

        services.AddRepositoryWorkspace(settings.Workspace);
        services.AddOptions<WorkspaceExecutionOptions>()
            .Configure<WorkspaceSelection>((options, selection) =>
            {
                options.RepositoryRoot = selection.Workspace.RootPath;
                options.WorkspaceId = selection.Definition.Id.ToString();
                options.WorkspaceSources = selection.Definition.Sources;
            });

        return services;
    }
}
