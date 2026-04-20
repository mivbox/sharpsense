using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharpSense.Infrastructure.Storage;

public static class RepositoryWorkspaceServiceCollectionExtensions
{
    public static IServiceCollection AddRepositoryWorkspace(
        this IServiceCollection services,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        services.TryAddSingleton<IRepositoryWorkspace>(
            RepositoryWorkspace.CreateFromWorkingDirectory(workingDirectory));

        return services;
    }
}
