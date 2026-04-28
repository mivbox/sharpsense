using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Storage;

public static class FileSystemServiceCollectionExtensions
{
    public static IServiceCollection AddFileSystem(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton<IFileSystemWatcherFactory>(serviceProvider =>
            serviceProvider.GetRequiredService<IFileSystem>().FileSystemWatcher);

        return services;
    }
}
