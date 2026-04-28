using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.IntegrationTests;

public sealed class SharpSenseConfigurationExtensionsTests
{
    [Fact]
    public void WhenLoadingTargetDirectoryConfig_ThenReturnsTrimmedIncludePaths()
    {
        const string targetDirectory = "/repo/src/Sample";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/src/Sample/sharpsense.yaml"] = new(
                """
                includePaths:
                  - docs/**/*.md
                  - " README.md "
                """)
        }, "/repo");
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddSingleton<IOptionsChangeTokenSource<SharpSenseConfig>, ManualSharpSenseConfigChangeTokenSource>();
        services.AddSharpSenseConfiguration(targetDirectory);

        using var serviceProvider = services.BuildServiceProvider();
        var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<SharpSenseConfig>>();

        monitor.CurrentValue.IncludePaths.Should().Equal("docs/**/*.md", "README.md");
    }

    [Fact]
    public void WhenLoadingConfigWithoutFile_ThenReturnsEmptyConfig()
    {
        const string targetDirectory = "/repo/src/Sample";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), "/repo");
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddSingleton<IOptionsChangeTokenSource<SharpSenseConfig>, ManualSharpSenseConfigChangeTokenSource>();
        services.AddSharpSenseConfiguration(targetDirectory);

        using var serviceProvider = services.BuildServiceProvider();
        var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<SharpSenseConfig>>();

        monitor.CurrentValue.IncludePaths.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenConfigChanges_ThenOptionsMonitorReturnsUpdatedConfig()
    {
        const string targetDirectory = "/repo/src/Sample";
        const string configPath = "/repo/src/Sample/sharpsense.yaml";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [configPath] = new(
                """
                includePaths:
                  - docs/**/*.md
                """)
        }, "/repo");
        var changeTokenSource = new ManualSharpSenseConfigChangeTokenSource();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddSingleton<IOptionsChangeTokenSource<SharpSenseConfig>>(changeTokenSource);
        services.AddSharpSenseConfiguration(targetDirectory);

        using var serviceProvider = services.BuildServiceProvider();
        var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<SharpSenseConfig>>();
        var updatedConfigSource = new TaskCompletionSource<SharpSenseConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var changeRegistration = monitor.OnChange(updatedConfig =>
        {
            if (updatedConfig.IncludePaths.SequenceEqual(["notes/**/*.md"]))
            {
                updatedConfigSource.TrySetResult(updatedConfig);
            }
        });

        monitor.CurrentValue.IncludePaths.Should().Equal("docs/**/*.md");
        fileSystem.File.WriteAllText(
            configPath,
            """
            includePaths:
              - notes/**/*.md
            """);
        changeTokenSource.SignalChange();

        var updatedConfig = await updatedConfigSource.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        updatedConfig.IncludePaths.Should().Equal("notes/**/*.md");
        monitor.CurrentValue.IncludePaths.Should().Equal("notes/**/*.md");
    }

    private sealed class ManualSharpSenseConfigChangeTokenSource : IOptionsChangeTokenSource<SharpSenseConfig>
    {
        private CancellationTokenSource _cts = new();

        public string Name => Options.DefaultName;

        public IChangeToken GetChangeToken()
            => new CancellationChangeToken(_cts.Token);

        public void SignalChange()
        {
            var next = new CancellationTokenSource();
            var current = Interlocked.Exchange(ref _cts, next);
            current.Cancel();
            current.Dispose();
        }
    }
}
