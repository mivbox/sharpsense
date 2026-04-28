using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Storage;

public static class RepositoryWorkspaceServiceCollectionExtensions
{
    public static IServiceCollection AddRepositoryWorkspace(
        this IServiceCollection services,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        services.AddFileSystem();
        services.TryAddSingleton<IRepositoryWorkspaceFactory, RepositoryWorkspaceFactory>();
        services.TryAddSingleton<IRepositoryWorkspace>(ctx =>
            ctx.GetRequiredService<IRepositoryWorkspaceFactory>()
                .CreateFromWorkingDirectory(workingDirectory));

        return services;
    }
}
