using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.IO.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SharpSense.Infrastructure.Storage;

public static class SharpSenseConfigurationExtensions
{
    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static IServiceCollection AddSharpSenseConfiguration(
        this IServiceCollection services,
        string targetDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        var normalizedPhysicalTargetDirectory = NormalizePhysicalTargetDirectory(targetDirectory);

        services.AddFileSystem();
        services.AddOptions<SharpSenseConfig>()
            .Configure<IFileSystem>((options, fileSystem) =>
            {
                var configPath = fileSystem.Path.Combine(normalizedPhysicalTargetDirectory, "sharpsense.yaml");
                if (!fileSystem.File.Exists(configPath))
                {
                    return;
                }

                using var configReader = fileSystem.File.OpenText(configPath);
                var config = _yamlDeserializer.Deserialize<SharpSenseConfig>(configReader) ?? new SharpSenseConfig();
                var includePaths = config.IncludePaths ?? [];

                options.IncludePaths =
                [
                    .. includePaths
                        .Where(static includePath => !string.IsNullOrWhiteSpace(includePath))
                        .Select(static includePath => includePath.Trim())
                ];
            });

        services.TryAddSingleton<IOptionsChangeTokenSource<SharpSenseConfig>>(
            _ => new SharpSenseConfigChangeTokenSource(normalizedPhysicalTargetDirectory));

        return services;
    }

    private sealed class SharpSenseConfigChangeTokenSource(string targetDirectory)
        : IOptionsChangeTokenSource<SharpSenseConfig>, IDisposable
    {
        private readonly PhysicalFileProvider _fileProvider = CreateFileProvider(targetDirectory);

        public string Name => Options.DefaultName;

        public IChangeToken GetChangeToken()
            => _fileProvider.Watch("sharpsense.yaml");

        public void Dispose()
            => _fileProvider.Dispose();

        private static PhysicalFileProvider CreateFileProvider(string targetDirectory)
            => new(targetDirectory) // It's already normalized by the time it gets here
            {
                UsePollingFileWatcher = true
            };
    }

    private static string NormalizePhysicalTargetDirectory(string targetDirectory)
    {
        var fullPath = Path.GetFullPath(targetDirectory);

        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        return Path.TrimEndingDirectorySeparator(fullPath);
    }
}
