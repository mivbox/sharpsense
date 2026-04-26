using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.IntegrationTests;

public sealed class SharpSenseConfigurationExtensionsTests
{
    [Fact]
    public void WhenLoadingConfigFromTargetDirectory_ThenReturnsTrimmedIncludePaths()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var targetDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var repositoryConfigPath = Path.Combine(repositoryRoot, "sharpsense.yaml");
        var targetConfigPath = Path.Combine(targetDirectory, "sharpsense.yaml");

        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(
            repositoryConfigPath,
            """
            includePaths:
              - notes/**/*.md
            """);
        File.WriteAllText(
            targetConfigPath,
            """
            includePaths:
              - docs/**/*.md
              - " README.md "
            """);

        try
        {
            var services = new ServiceCollection();
            services.AddSharpSenseConfiguration(targetDirectory);
            using var serviceProvider = services.BuildServiceProvider();
            var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<SharpSenseConfig>>();

            monitor.CurrentValue.IncludePaths.Should().Equal("docs/**/*.md", "README.md");
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    [Fact]
    public void WhenLoadingConfigWithoutFile_ThenReturnsEmptyConfig()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var targetDirectory = Path.Combine(repositoryRoot, "src", "Sample");

        Directory.CreateDirectory(targetDirectory);

        try
        {
            var services = new ServiceCollection();
            services.AddSharpSenseConfiguration(targetDirectory);
            using var serviceProvider = services.BuildServiceProvider();
            var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<SharpSenseConfig>>();

            monitor.CurrentValue.IncludePaths.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    [Fact]
    public async Task WhenTargetDirectoryConfigChanges_ThenOptionsMonitorReturnsUpdatedConfig()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var targetDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var configPath = Path.Combine(targetDirectory, "sharpsense.yaml");

        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(
            configPath,
            """
            includePaths:
              - docs/**/*.md
            """);

        try
        {
            var services = new ServiceCollection();
            services.AddSharpSenseConfiguration(targetDirectory);

            using var serviceProvider = services.BuildServiceProvider();
            var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<SharpSenseConfig>>();

            monitor.CurrentValue.IncludePaths.Should().Equal("docs/**/*.md");

            var updatedConfigSource = new TaskCompletionSource<SharpSenseConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var changeRegistration = monitor.OnChange(updatedConfig =>
            {
                if (updatedConfig.IncludePaths.SequenceEqual(["notes/**/*.md"]))
                {
                    updatedConfigSource.TrySetResult(updatedConfig);
                }
            });

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

            File.WriteAllText(
                configPath,
                """
                includePaths:
                  - notes/**/*.md
                """);

            var updatedConfig = await updatedConfigSource.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

            updatedConfig.IncludePaths.Should().Equal("notes/**/*.md");
            monitor.CurrentValue.IncludePaths.Should().Equal("notes/**/*.md");
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    private static string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"sharp-sense-cli-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, ".git"));
        return repositoryRoot;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
