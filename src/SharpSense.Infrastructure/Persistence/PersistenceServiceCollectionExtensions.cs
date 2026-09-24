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

    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        bool initializeOnStartup = true,
        ServiceLifetime factoryLifetime = ServiceLifetime.Singleton)
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
            }, factoryLifetime);

        services.AddScoped<WorkspaceDatabaseInitializer>();
        if (initializeOnStartup)
        {
            services.AddHostedService<EfCoreEnsureDatabase>();
        }

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
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SharpSenseDbContext>>();
            await InitializeAsync(repositoryWorkspace, dbContextFactory, fileSystem, ct);
        }

        internal static async Task InitializeAsync(
            IRepositoryWorkspace repositoryWorkspace,
            IDbContextFactory<SharpSenseDbContext> dbContextFactory,
            IFileSystem fileSystem,
            CancellationToken ct)
        {
            var databasePath = repositoryWorkspace.DatabasePath;

            var directory = fileSystem.Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory) && !fileSystem.Directory.Exists(directory))
            {
                fileSystem.Directory.CreateDirectory(directory);
            }

            var resetReason = await GetResetReason(dbContextFactory, databasePath, fileSystem, ct);
            if (resetReason is not null)
            {
                throw new InvalidOperationException(
                    $"SQLite database '{databasePath}' is incompatible with this version ({resetReason}). " +
                    "The database has been preserved. Use a compatible SharpSense version, or back up " +
                    "the database and export any authored memories before rebuilding the index.");
            }

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
            await dbContext.Database.MigrateAsync(ct);
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

        private static async Task<string?> GetResetReason(
            IDbContextFactory<SharpSenseDbContext> dbContextFactory,
            string databasePath,
            IFileSystem fileSystem,
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
