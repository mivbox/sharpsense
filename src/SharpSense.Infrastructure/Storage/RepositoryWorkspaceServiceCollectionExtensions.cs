using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Storage;

public static class RepositoryWorkspaceServiceCollectionExtensions
{
    public static IServiceCollection AddRepositoryWorkspace(
        this IServiceCollection services,
        string workingDirectory,
        string? workspaceNameOrId = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        services.AddFileSystem();
        services.TryAddSingleton(provider => new WorkspaceCatalog(provider.GetRequiredService<IFileSystem>()));
        services.TryAddSingleton(provider => provider.GetRequiredService<WorkspaceCatalog>()
            .Resolve(workspaceNameOrId, workingDirectory));
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
