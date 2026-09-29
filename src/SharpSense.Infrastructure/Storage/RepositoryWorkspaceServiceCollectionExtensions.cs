using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharpSense.Infrastructure.Storage;

public static class RepositoryWorkspaceServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaceCatalog(this IServiceCollection services)
    {
        services.AddFileSystem();
        services.TryAddSingleton<IWorkspaceCatalog, WorkspaceCatalog>();
        services.TryAddScoped<IWorkspaceScope, WorkspaceScope>();

        return services;
    }

    public static IServiceCollection AddRepositoryWorkspace(
        this IServiceCollection services,
        string? workspaceNameOrId = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddWorkspaceCatalog();
        services.TryAddSingleton(provider => provider.GetRequiredService<IWorkspaceCatalog>()
            .Resolve(workspaceNameOrId));
        services.TryAddSingleton(provider => provider.GetRequiredService<WorkspaceSelection>().Workspace);

        return services;
    }

    public static IServiceCollection AddRepositoryWorkspace(
        this IServiceCollection services,
        WorkspaceSelection selection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(selection);

        services.AddFileSystem();
        services.AddSingleton(selection);
        services.AddSingleton(selection.Workspace);

        return services;
    }
}
