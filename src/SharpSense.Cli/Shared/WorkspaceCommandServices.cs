using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Shared.Options;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Shared;

internal static class WorkspaceCommandServices
{
    public static IServiceCollection AddSelectedWorkspace(this IServiceCollection services, GlobalSettings settings)
    {
        var workingDirectory = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);
        services.AddRepositoryWorkspace(workingDirectory, settings.Workspace);
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
