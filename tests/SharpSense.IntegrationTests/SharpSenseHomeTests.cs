using System.IO.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.IntegrationTests;

public sealed class SharpSenseHomeTests
{
    [Fact]
    public void LoggingAndCatalogUseSameExplicitHomeWithoutWritingDuringPathResolution()
    {
        var directory = Directory.CreateTempSubdirectory("sharpsense-home-tests-");
        try
        {
            var home = Path.Combine(directory.FullName, "isolated-home");
            using var homeOverride = new HomeOverride(home);
            var catalog = new WorkspaceCatalog(new FileSystem());
            var logPath = SharpSenseLogging.GetLogFilePath("Workspace Create");

            Assert.Equal(home, catalog.HomeDirectory);
            Assert.Equal(Path.Combine(home, "logs", "workspace-create.log"), logPath);
            Assert.False(Directory.Exists(home));

            var logger = SharpSenseLogging.CreateLogger(false, "Workspace Create", enableConsoleLogging: false);
            try
            {
                logger.Information("Isolated workspace log entry");
            }
            finally
            {
                ((IDisposable)logger).Dispose();
            }

            Assert.Contains("Isolated workspace log entry", File.ReadAllText(logPath));
            Assert.False(Directory.Exists(Path.Combine(home, "workspaces")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DefaultLogPathUsesLowercaseHomeWithoutCreatingDirectories()
    {
        using var homeOverride = new HomeOverride(null);

        var logPath = SharpSenseLogging.GetLogFilePath("analyze");

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".sharpsense", "logs", "analyze.log"), logPath);
    }

    [Fact]
    public void RelativeHomeOverrideIsRejectedByCatalogLoggingAndDesignTimeFactory()
    {
        using var homeOverride = new HomeOverride("relative/sharpsense-home");

        var catalogError = Assert.Throws<ArgumentException>(() => new WorkspaceCatalog(new FileSystem()));
        var loggingError = Assert.Throws<ArgumentException>(() => SharpSenseLogging.GetLogFilePath("analyze"));
        var designTimeError = Assert.Throws<ArgumentException>(() => new SharpSenseDesignTimeDbContextFactory().CreateDbContext([]));

        Assert.Contains("SHARPSENSE_HOME must be an absolute", catalogError.Message);
        Assert.Equal(catalogError.Message, loggingError.Message);
        Assert.Equal(catalogError.Message, designTimeError.Message);
    }

    [Fact]
    public void DesignTimeFactoryUsesIsolatedArtifactWithoutCreatingWorkspaceOrDatabase()
    {
        var directory = Directory.CreateTempSubdirectory("sharpsense-design-time-tests-");
        try
        {
            using var homeOverride = new HomeOverride(directory.FullName);
            using var context = new SharpSenseDesignTimeDbContextFactory().CreateDbContext([]);
            var connection = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());

            Assert.Equal(Path.Combine(directory.FullName, "sharpsense-design-time.db"), connection.DataSource);
            Assert.Empty(directory.EnumerateFileSystemInfos());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DefaultDesignTimeArtifactUsesTemporaryDirectory()
    {
        using var homeOverride = new HomeOverride(null);
        using var context = new SharpSenseDesignTimeDbContextFactory().CreateDbContext([]);

        var connection = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());

        Assert.Equal(Path.Combine(Path.GetTempPath(), "sharpsense-design-time.db"), connection.DataSource);
    }

    private sealed class HomeOverride : IDisposable
    {
        private readonly string? _previousValue = Environment.GetEnvironmentVariable("SHARPSENSE_HOME");

        public HomeOverride(string? value) => Environment.SetEnvironmentVariable("SHARPSENSE_HOME", value);

        public void Dispose() => Environment.SetEnvironmentVariable("SHARPSENSE_HOME", _previousValue);
    }
}
