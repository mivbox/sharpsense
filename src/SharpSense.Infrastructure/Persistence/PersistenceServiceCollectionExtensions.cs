using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    private static readonly ILogger _logger = Log.ForContext(typeof(PersistenceServiceCollectionExtensions));

    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFileSystem();
        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddDbContextFactory<SharpSenseDbContext>(
            (serviceProvider, options) =>
            {
                var repositoryWorkspace = serviceProvider.GetRequiredService<IRepositoryWorkspace>();
                _logger.Information(
                    "Configuring SQLite database context for {DatabasePath}",
                    repositoryWorkspace.DatabasePath);

                options.UseSqlite(
                        $"Data Source={repositoryWorkspace.DatabasePath};Mode=ReadWriteCreate;Cache=Shared",
                        b => b.MigrationsAssembly(typeof(SharpSenseDbContext).Assembly.FullName))
                    .AddInterceptors(serviceProvider.GetRequiredService<SqlitePragmaInterceptor>());
            });

        services.AddHostedService<EfCoreEnsureDatabase>();

        return services;
    }

    internal class EfCoreEnsureDatabase(
        IServiceProvider serviceProvider,
        IFileSystem fileSystem) : IHostedService
    {
        private const string MigrationHistoryTableName = "__EFMigrationsHistory";

        public async Task StartAsync(CancellationToken ct)
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var repositoryWorkspace = scope.ServiceProvider.GetRequiredService<IRepositoryWorkspace>();
            var databasePath = repositoryWorkspace.DatabasePath;

            var directory = fileSystem.Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory) && !fileSystem.Directory.Exists(directory))
            {
                fileSystem.Directory.CreateDirectory(directory);
            }

            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SharpSenseDbContext>>();
            var resetReason = await GetResetReason(dbContextFactory, databasePath, ct);
            if (resetReason is not null)
            {
                _logger.Warning(
                    "Deleting incompatible SQLite database at {DatabasePath}: {Reason}",
                    databasePath,
                    resetReason);

                SqliteConnection.ClearAllPools();
                DeleteDatabaseFiles(databasePath);
                SqliteConnection.ClearAllPools();
            }

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
            await dbContext.Database.MigrateAsync(ct);
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

        private async Task<string?> GetResetReason(
            IDbContextFactory<SharpSenseDbContext> dbContextFactory,
            string databasePath,
            CancellationToken ct)
        {
            if (!fileSystem.File.Exists(databasePath))
            {
                return null;
            }

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
            var currentMigrationIds = dbContext.GetService<IMigrationsAssembly>()
                .Migrations.Keys
                .ToHashSet(StringComparer.Ordinal);

            await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite;Cache=Shared");
            await connection.OpenAsync(ct);

            var userTableNames = await GetUserTableNames(connection, ct);
            if (userTableNames.Count == 0)
            {
                return null;
            }

            var hasNonHistoryTables = userTableNames.Any(
                static tableName => !string.Equals(tableName, MigrationHistoryTableName, StringComparison.Ordinal));
            if (!hasNonHistoryTables)
            {
                return null;
            }

            if (!userTableNames.Contains(MigrationHistoryTableName))
            {
                return "database contains user tables without migration history";
            }

            var appliedMigrationIds = await GetAppliedMigrationIds(connection, ct);
            if (appliedMigrationIds.Count == 0)
            {
                return "database contains user tables without compatible migration history";
            }

            var unknownMigrationIds = appliedMigrationIds
                .Where(migrationId => !currentMigrationIds.Contains(migrationId))
                .OrderBy(static migrationId => migrationId, StringComparer.Ordinal)
                .ToArray();

            return unknownMigrationIds.Length == 0
                ? null
                : $"database contains migration ids not present in the current assembly: {string.Join(", ", unknownMigrationIds)}";
        }

        private void DeleteDatabaseFiles(string databasePath)
        {
            foreach (var candidatePath in GetDatabaseFilePaths(databasePath))
            {
                if (fileSystem.File.Exists(candidatePath))
                {
                    fileSystem.File.Delete(candidatePath);
                }
            }
        }

        private static IEnumerable<string> GetDatabaseFilePaths(string databasePath)
        {
            yield return databasePath;
            yield return $"{databasePath}-wal";
            yield return $"{databasePath}-shm";
        }

        private static async Task<HashSet<string>> GetUserTableNames(
            SqliteConnection connection,
            CancellationToken ct)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%';
                """;

            var tableNames = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tableNames.Add(reader.GetString(0));
            }

            return tableNames;
        }

        private static async Task<HashSet<string>> GetAppliedMigrationIds(
            SqliteConnection connection,
            CancellationToken ct)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT "MigrationId"
                FROM "{MigrationHistoryTableName}";
                """;

            var migrationIds = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                migrationIds.Add(reader.GetString(0));
            }

            return migrationIds;
        }
    }
}
