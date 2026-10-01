using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Tests.Persistence;

public sealed class PersistenceServiceCollectionExtensionsTests
{
    [Fact]
    public async Task WhenStartingWithUnknownMigrationHistory_ThenPreservesDatabaseAndReportsIncompatibility()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("sharp-sense-persistence-");

        try
        {
            var databasePath = Path.Combine(tempDirectory.FullName, "sharpsense.db");

            await CreateLegacyDatabase(databasePath, includeMigrationHistory: true);
            await AssertDatabaseWasPreserved(databasePath);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WhenStartingWithUserTablesWithoutMigrationHistory_ThenPreservesDatabase()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("sharp-sense-persistence-");

        try
        {
            var databasePath = Path.Combine(tempDirectory.FullName, "sharpsense.db");

            await CreateLegacyDatabase(databasePath, includeMigrationHistory: false);
            await AssertDatabaseWasPreserved(databasePath);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static async Task AssertDatabaseWasPreserved(string databasePath)
    {
        using var serviceProvider = CreateServiceProvider(databasePath);
        var startup = new WorkspaceDatabaseStartup(
            serviceProvider,
            new FileSystem());
        var start = () => startup.StartAsync(TestContext.Current.CancellationToken);
        await start.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*has been preserved*");
        (await ReadScalarInt(
            databasePath,
            "SELECT COUNT(*) FROM pragma_table_info('CodeNodes') WHERE name = 'RelativeFilePath';"))
            .Should().Be(1);
        (await ReadStrings(databasePath, "SELECT Content FROM MemoryNodes;"))
            .Should().Equal("Irreplaceable authored context");
    }

    [Fact]
    public async Task WhenStartingWithNewDatabase_ThenAppliesCurrentMigrations()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDirectory = Directory.CreateTempSubdirectory("sharp-sense-persistence-");
        try
        {
            var databasePath = Path.Combine(tempDirectory.FullName, "sharpsense.db");
            using var serviceProvider = CreateServiceProvider(databasePath);
            var startup = new WorkspaceDatabaseStartup(
                serviceProvider,
                new FileSystem());
            await startup.StartAsync(ct);
            await startup.StartAsync(ct);
            (await ReadStrings(databasePath, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;"))
                .Should().Equal(await GetExpectedMigrationIds(serviceProvider));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static ServiceProvider CreateServiceProvider(string databasePath)
    {
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace
            .SetupGet(candidate => candidate.DatabasePath)
            .Returns(databasePath);

        var services = new ServiceCollection();
        services.AddSingleton(repositoryWorkspace.Object);
        services.AddDbContextFactory<SharpSenseDbContext>(
            (serviceProvider, options) =>
            {
                var workspace = serviceProvider.GetRequiredService<IRepositoryWorkspace>();
                options.UseSqlite(
                    $"Data Source={workspace.DatabasePath};Mode=ReadWriteCreate;Cache=Shared",
                    sqlite => sqlite.MigrationsAssembly(typeof(SharpSenseDbContext).Assembly.FullName));
            });

        return services.BuildServiceProvider();
    }

    private static async Task<string[]> GetExpectedMigrationIds(IServiceProvider serviceProvider)
    {
        var dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<SharpSenseDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        return dbContext.GetService<IMigrationsAssembly>().Migrations.Keys
            .OrderBy(static migrationId => migrationId, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task CreateLegacyDatabase(
        string databasePath,
        bool includeMigrationHistory)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = includeMigrationHistory
            ? """
                CREATE TABLE "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260426022637_Initial', '10.0.6');
                CREATE TABLE "CodeNodes" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_CodeNodes" PRIMARY KEY,
                    "RelativeFilePath" TEXT NOT NULL
                );
                """
            : """
                CREATE TABLE "CodeNodes" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_CodeNodes" PRIMARY KEY,
                    "RelativeFilePath" TEXT NOT NULL
                );
                """;

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        command.CommandText = "CREATE TABLE MemoryNodes (Content TEXT NOT NULL); INSERT INTO MemoryNodes VALUES ('Irreplaceable authored context');";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<string[]> ReadStrings(
        string databasePath,
        string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite;Cache=Shared");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static async Task<int> ReadScalarInt(
        string databasePath,
        string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite;Cache=Shared");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
