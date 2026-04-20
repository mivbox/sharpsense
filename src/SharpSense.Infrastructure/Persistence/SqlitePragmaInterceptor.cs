using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SharpSense.Infrastructure.Persistence;

public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string _pragmaSql = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is SqliteConnection sqliteConnection)
        {
            LoadVectorExtension(sqliteConnection);
            ExecutePragmas(connection);
        }

        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken ct = default)
    {
        if (connection is SqliteConnection sqliteConnection)
        {
            LoadVectorExtension(sqliteConnection);
            await ExecutePragmas(connection, ct);
        }

        await base.ConnectionOpenedAsync(connection, eventData, ct);
    }

    private static void ExecutePragmas(DbConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = _pragmaSql;
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 8) // SQLITE_READONLY
        {
            // EF Core frequently opens temporary read-only "probe" connections to check
            // migration history. We safely ignore setting PRAGMAs on these probes.
        }
    }

    private static void LoadVectorExtension(SqliteConnection connection)
    {
        try
        {
            connection.EnableExtensions();
            connection.LoadVector();
            connection.EnableExtensions(false);
        }
        catch (Exception e)
        {
            // GTFO, this should never happen, but if we cannot load sqlite-vec extension we cannot run the application
            throw new InvalidOperationException(
                "Failed to load sqlite-vec extension into the SQLite connection.", e);
        }
    }

    private static async Task ExecutePragmas(
        DbConnection connection,
        CancellationToken ct)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = _pragmaSql;
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 8) // SQLITE_READONLY
        {
            // EF Core frequently opens temporary read-only "probe" connections to check
            // migration history. We safely ignore setting PRAGMAs on these probes.
        }
    }
}
