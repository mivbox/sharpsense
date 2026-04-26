using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
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

        var normalizedTargetDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory));

        services.AddOptions<SharpSenseConfig>()
            .Configure(options =>
            {
                var configPath = Path.Combine(normalizedTargetDirectory, "sharpsense.yaml");
                if (!File.Exists(configPath))
                {
                    return;
                }

                using var configReader = File.OpenText(configPath);
                var config = _yamlDeserializer.Deserialize<SharpSenseConfig>(configReader) ?? new SharpSenseConfig();
                var includePaths = config.IncludePaths ?? [];

                options.IncludePaths =
                [
                    .. includePaths
                        .Where(static includePath => !string.IsNullOrWhiteSpace(includePath))
                        .Select(static includePath => includePath.Trim())
                ];
            });
        services.AddSingleton<IOptionsChangeTokenSource<SharpSenseConfig>>(
            _ => new SharpSenseConfigChangeTokenSource(normalizedTargetDirectory));

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
            => new(Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory)))
            {
                UsePollingFileWatcher = true
            };
    }
}
