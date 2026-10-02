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
    public async Task WhenDatabasePathContainsConnectionStringDelimiters_ThenInitializesThatExactFile()
    {
        var directory = Directory.CreateTempSubdirectory("sharpsense-path-");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var databasePath = Path.Combine(directory.FullName, "home;segment", "index.db");
            var services = new ServiceCollection();
            services.AddSingleton<IRepositoryWorkspace>(new RepositoryWorkspace(directory.FullName, databasePath, new FileSystem()));
            services.AddPersistence(initializeOnStartup: false);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<IWorkspaceDatabaseInitializer>().Initialize(ct);
            await new WorkspaceDatabaseStartup(provider, new FileSystem()).StartAsync(ct);

            File.Exists(databasePath).Should().BeTrue();
            await using var context = await provider.GetRequiredService<IDbContextFactory<SharpSenseDbContext>>().CreateDbContextAsync(ct);
            (await context.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
            (await context.CodeNodes.CountAsync(ct)).Should().Be(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WhenStartingWithUnknownMigrationHistory_ThenPreservesDatabaseAndReportsIncompatibility()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("sharp-sense-persistence-");

        try
        {
            var databasePath = Path.Combine(tempDirectory.FullName, "sharpsense.db");

            await CreateLegacyDatabase(databasePath, includeMigrationHistory: true, ct: TestContext.Current.CancellationToken);
            await AssertDatabaseWasPreserved(databasePath, TestContext.Current.CancellationToken);
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

            await CreateLegacyDatabase(databasePath, includeMigrationHistory: false, ct: TestContext.Current.CancellationToken);
            await AssertDatabaseWasPreserved(databasePath, TestContext.Current.CancellationToken);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static async Task AssertDatabaseWasPreserved(string databasePath, CancellationToken ct)
    {
        using var serviceProvider = CreateServiceProvider(databasePath);
        var startup = new WorkspaceDatabaseStartup(
            serviceProvider,
            new FileSystem());
        var start = () => startup.StartAsync(ct);
        await start.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*has been preserved*");
        (await ReadScalarInt(
            databasePath,
            "SELECT COUNT(*) FROM pragma_table_info('CodeNodes') WHERE name = 'RelativeFilePath';",
            ct))
            .Should().Be(1);
        (await ReadStrings(databasePath, "SELECT Content FROM MemoryNodes;", ct))
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
            (await ReadStrings(databasePath, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;", ct))
                .Should().Equal(await GetExpectedMigrationIds(serviceProvider, ct));
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

    private static async Task<string[]> GetExpectedMigrationIds(IServiceProvider serviceProvider, CancellationToken ct)
    {
        var dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<SharpSenseDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return dbContext.GetService<IMigrationsAssembly>().Migrations.Keys
            .OrderBy(static migrationId => migrationId, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task CreateLegacyDatabase(
        string databasePath,
        bool includeMigrationHistory,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared");
        await connection.OpenAsync(ct);

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

        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "CREATE TABLE MemoryNodes (Content TEXT NOT NULL); INSERT INTO MemoryNodes VALUES ('Irreplaceable authored context');";
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string[]> ReadStrings(
        string databasePath,
        string sql,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite;Cache=Shared");
        await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static async Task<int> ReadScalarInt(
        string databasePath,
        string sql,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite;Cache=Shared");
        await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(ct),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
