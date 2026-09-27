using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.IntegrationTests;

public sealed class SharpSenseHomeTests
{
    [Fact]
    public void WhenLoggingAndCatalog_ThenUseSameExplicitHomeWithoutWritingDuringPathResolution()
    {
        var directory = Directory.CreateTempSubdirectory("sharpsense-home-tests-");
        try
        {
            var home = Path.Combine(directory.FullName, "isolated-home");
            using var homeOverride = new HomeOverride(home);
            var catalog = new WorkspaceCatalog(new FileSystem());
            var logPath = SharpSenseLogging.GetLogFilePath("Workspace Create");

            catalog.HomeDirectory.Should().Be(home);
            logPath.Should().Be(Path.Combine(home, "logs", "workspace-create.log"));
            Directory.Exists(home).Should().BeFalse();

            var logger = SharpSenseLogging.CreateLogger(false, "Workspace Create", enableConsoleLogging: false);
            try
            {
                logger.Information("Isolated workspace log entry");
            }
            finally
            {
                ((IDisposable)logger).Dispose();
            }

            File.ReadAllText(logPath).Should().Contain("Isolated workspace log entry");
            Directory.Exists(Path.Combine(home, "workspaces")).Should().BeFalse();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void WhenDefaultLogPath_ThenUsesLowercaseHomeWithoutCreatingDirectories()
    {
        using var homeOverride = new HomeOverride(null);

        var logPath = SharpSenseLogging.GetLogFilePath("analyze");

        logPath.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".sharpsense",
            "logs",
            "analyze.log"));
    }

    [Fact]
    public void WhenRelativeHomeOverride_ThenIsRejectedByCatalogLoggingAndDesignTimeFactory()
    {
        using var homeOverride = new HomeOverride("relative/sharpsense-home");

        var catalogError = ((Action)(() => new WorkspaceCatalog(new FileSystem()))).Should().ThrowExactly<ArgumentException>().Which;
        var loggingError = ((Action)(() => SharpSenseLogging.GetLogFilePath("analyze"))).Should().ThrowExactly<ArgumentException>().Which;
        var designTimeError = ((Action)(() => new SharpSenseDesignTimeDbContextFactory().CreateDbContext([]))).Should().ThrowExactly<ArgumentException>().Which;

        catalogError.Message.Should().Contain("SHARPSENSE_HOME must be an absolute");
        loggingError.Message.Should().Be(catalogError.Message);
        designTimeError.Message.Should().Be(catalogError.Message);
    }

    [Fact]
    public void WhenDesignTimeFactory_ThenUsesIsolatedArtifactWithoutCreatingWorkspaceOrDatabase()
    {
        var directory = Directory.CreateTempSubdirectory("sharpsense-design-time-tests-");
        try
        {
            using var homeOverride = new HomeOverride(directory.FullName);
            using var context = new SharpSenseDesignTimeDbContextFactory().CreateDbContext([]);
            var connection = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());

            connection.DataSource.Should().Be(Path.Combine(directory.FullName, "sharpsense-design-time.db"));
            directory.EnumerateFileSystemInfos().Should().BeEmpty();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void WhenDefaultDesignTimeArtifact_ThenUsesTemporaryDirectory()
    {
        using var homeOverride = new HomeOverride(null);
        using var context = new SharpSenseDesignTimeDbContextFactory().CreateDbContext([]);

        var connection = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());

        connection.DataSource.Should().Be(Path.Combine(Path.GetTempPath(), "sharpsense-design-time.db"));
    }

    private sealed class HomeOverride : IDisposable
    {
        private readonly string? _previousValue = Environment.GetEnvironmentVariable("SHARPSENSE_HOME");

        public HomeOverride(string? value) => Environment.SetEnvironmentVariable("SHARPSENSE_HOME", value);

        public void Dispose() => Environment.SetEnvironmentVariable("SHARPSENSE_HOME", _previousValue);
    }
}
