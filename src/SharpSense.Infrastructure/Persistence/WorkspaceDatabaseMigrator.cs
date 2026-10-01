using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Persistence;

internal static class WorkspaceDatabaseMigrator
{
    private const string MigrationHistoryTableName = "__EFMigrationsHistory";

    internal static async Task Initialize(
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

        var incompatibilityReason = await GetIncompatibilityReason(dbContextFactory, databasePath, fileSystem, ct);
        if (incompatibilityReason is not null)
        {
            throw new InvalidOperationException(
                $"SQLite database '{databasePath}' is incompatible with this version ({incompatibilityReason}). " +
                "The database has been preserved. Use a compatible SharpSense version, or back up " +
                "the database and export any authored memories before rebuilding the index.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        await dbContext.Database.MigrateAsync(ct);
    }

    private static async Task<string?> GetIncompatibilityReason(
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
        var currentMigrationIds = dbContext.GetService<IMigrationsAssembly>().Migrations.Keys
            .ToHashSet(StringComparer.Ordinal);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite;Cache=Shared");
        await connection.OpenAsync(ct);

        var userTableNames = await GetUserTableNames(connection, ct);
        if (userTableNames.Count == 0)
        {
            return null;
        }

        var hasNonHistoryTables = userTableNames
            .Any(
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
            : $"database contains migration ids not present in the current assembly: {string.Join(
                ", ",
                unknownMigrationIds)}";
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
