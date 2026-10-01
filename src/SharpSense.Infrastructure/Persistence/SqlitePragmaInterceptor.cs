using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace SharpSense.Infrastructure.Persistence;

internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
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
            // Vector queries require this extension; startup must fail if it cannot be loaded.
            throw new InvalidOperationException(
                "Failed to load sqlite-vec extension into the SQLite connection.",
                e);
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
