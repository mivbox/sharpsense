using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.GraphStats.Abstractions;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Storage;
using System.Text.Json;

namespace SharpSense.Infrastructure.GraphStats;

internal sealed class GraphStatsReader(IRepositoryWorkspace workspace) : IGraphStatsReader
{
    private static readonly string[] _requiredTables =
    [
        "Directories", "Documents", "GraphNodes", "CodeNodes", "ProjectNodes",
        "MemoryNodes", "DependencyEdges", "DirectoryClosures"
    ];

    private static readonly HashSet<string> _knownMigrations = LoadKnownMigrations();

    private static HashSet<string> LoadKnownMigrations()
    {
        using var context = new SharpSenseDbContext(new DbContextOptionsBuilder<SharpSenseDbContext>()
            .UseSqlite("Data Source=:memory:").Options);

        return context.Database.GetMigrations()
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<GraphStatsSnapshot> Read(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(workspace.DatabasePath))
        {
            return Empty(
                "missing",
                new(
                    "database_missing",
                    "warning",
                    "This workspace has no index database.",
                    Suggestion: "Run 'sharpsense analyze --workspace <name>' to create its index."));
        }

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = workspace.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString();
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var transaction = connection.BeginTransaction(deferred: true);

            var tables = await ReadStrings(
                connection,
                transaction,
                "SELECT name FROM sqlite_master WHERE type = 'table';",
                ct);
            if (!tables.Contains("__EFMigrationsHistory") || _requiredTables.Any(table => !tables.Contains(table)))
            {
                return Incompatible("The database does not contain the expected index schema and migration history.");
            }

            var appliedMigrations = await ReadStrings(
                connection,
                transaction,
                "SELECT MigrationId FROM __EFMigrationsHistory;",
                ct);
            if (appliedMigrations.Count == 0 || appliedMigrations.Any(migration => !_knownMigrations.Contains(migration)))
            {
                return Incompatible("The database contains migration history that this version does not recognize.");
            }

            var obsoleteValue = await FindObsoleteEnumValue(connection, transaction, ct);
            if (obsoleteValue is not null)
            {
                return Incompatible($"The database contains an unsupported stored value: {obsoleteValue}.");
            }

            var diagnostics = new List<IndexDiagnostic>();
            var state = _knownMigrations.SetEquals(appliedMigrations) ? "ready" : "upgrade_required";
            if (state == "upgrade_required")
            {
                diagnostics.Add(new(
                    "schema_upgrade_required",
                    "warning",
                    "The index uses an earlier supported database schema.",
                    Suggestion: "Back up the database, then run 'sharpsense analyze --workspace <name>' to apply pending migrations and refresh the index."));
            }

            var counts = await ReadCounts(connection, transaction, ct);
            var languages = await ReadLanguages(connection, transaction, ct);
            var edgeTypes = await ReadEdgeTypes(connection, transaction, ct);
            var history = tables.Contains("IndexRunState")
                ? await ReadHistory(connection, transaction, ct)
                : new IndexRunHistory(null, null, []);
            diagnostics.AddRange(history.Diagnostics);

            if (history.LastSuccessfulIndex is null && counts.CodeNodes > 0)
            {
                diagnostics.Add(new(
                    "index_history_unavailable",
                    "info",
                    "The graph exists, but its last successful indexing time was not recorded.",
                    Suggestion: "Run indexing once with this version to record timings and diagnostic history."));
            }

            if (counts.CodeNodes == 0)
            {
                diagnostics.Add(new(
                    "index_empty",
                    "warning",
                    "The database contains no indexed code or document nodes.",
                    Suggestion: "Run 'sharpsense analyze --workspace <name>' and check its selected sources and exclusions."));
            }

            return new GraphStatsSnapshot(
                workspace.RootPath,
                workspace.DatabasePath,
                state,
                counts.CodeNodes > 0,
                counts.GraphNodes,
                counts.CodeNodes,
                counts.Edges,
                counts.Files,
                counts.Projects,
                counts.EmbeddedNodes,
                counts.Memories,
                languages,
                edgeTypes,
                history.LastSuccessfulIndex,
                history.LastAttempt,
                diagnostics,
                workspace.WorkspaceId,
                workspace.WorkspaceName);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            return Incompatible("The database schema cannot be read by this version.");
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return Empty(
                "unreadable",
                new(
                    "database_unreadable",
                    "error",
                    "The database could not be opened or read safely.",
                    Suggestion: "Check database permissions, available disk space, and whether another process holds a lock. Preserve a backup before attempting recovery."));
        }
    }

    private GraphStatsSnapshot Incompatible(string message)
        => Empty(
            "incompatible",
            new(
                "database_incompatible",
                "error",
                message,
                Suggestion: "The database has been preserved. Back it up and export authored memories using a compatible SharpSense version before rebuilding the index."));

    private GraphStatsSnapshot Empty(string state, IndexDiagnostic diagnostic)
        => new(
            workspace.RootPath,
            workspace.DatabasePath,
            state,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            [],
            null,
            null,
            [diagnostic],
            workspace.WorkspaceId,
            workspace.WorkspaceName);

    private static async Task<string?> FindObsoleteEnumValue(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken ct)
    {
        (string Table, string Column, string[] Values)[] checks =
        [
            ("DependencyEdges", "EdgeType", Enum.GetNames<EdgeType>()),
            ("CodeNodes", "NodeType", Enum.GetNames<NodeType>()),
            ("GraphNodes", "Kind", Enum.GetNames<GraphNodeKind>()),
            ("Documents", "Kind", Enum.GetNames<DocumentKind>())
        ];

        foreach (var (table, column, values) in checks)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var parameters = values.Select((value, index) => $"$value{index}")
                .ToArray();
            command.CommandText = $"SELECT substr({column}, 1, 100) FROM {table} WHERE {column} NOT IN ({string.Join(",", parameters)}) LIMIT 1;";
            for (var index = 0; index < values.Length; index++)
            {
                command.Parameters.AddWithValue(parameters[index], values[index]);
            }

            if (await command.ExecuteScalarAsync(ct) is string value)
            {
                return $"{table}.{column} = '{value}'";
            }
        }

        return null;
    }

    private static async Task<Counts> ReadCounts(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM GraphNodes),
                   (SELECT COUNT(*) FROM CodeNodes),
                   (SELECT COUNT(*) FROM DependencyEdges),
                   (SELECT COUNT(*) FROM Documents),
                   (SELECT COUNT(*) FROM ProjectNodes),
                   (SELECT COUNT(*) FROM CodeNodes WHERE length(VectorEmbedding) > 0),
                   (SELECT COUNT(*) FROM MemoryNodes);
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        return new(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6));
    }

    private static async Task<IReadOnlyList<LanguageStats>> ReadLanguages(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE lower(d.Extension)
                       WHEN '.cs' THEN 'C#'
                       WHEN '.ts' THEN 'TypeScript'
                       WHEN '.tsx' THEN 'TSX'
                       WHEN '.md' THEN 'Markdown'
                       WHEN '.markdown' THEN 'Markdown'
                       WHEN '.mdown' THEN 'Markdown'
                       WHEN '.mkd' THEN 'Markdown'
                       ELSE 'Other'
                   END AS Language,
                   COUNT(DISTINCT d.Id), COUNT(c.Id),
                   SUM(CASE WHEN length(c.VectorEmbedding) > 0 THEN 1 ELSE 0 END)
            FROM Documents d
            LEFT JOIN CodeNodes c ON c.DocumentId = d.Id
            GROUP BY Language
            ORDER BY Language;
            """;
        var languages = new List<LanguageStats>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            languages.Add(new(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)));
        }

        return languages;
    }

    private static async Task<IReadOnlyList<EdgeTypeStats>> ReadEdgeTypes(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EdgeType, COUNT(*) FROM DependencyEdges GROUP BY EdgeType ORDER BY EdgeType;";
        var edgeTypes = new List<EdgeTypeStats>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            edgeTypes.Add(new(reader.GetString(0), reader.GetInt64(1)));
        }

        return edgeTypes;
    }

    private static async Task<IndexRunHistory> ReadHistory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT substr(LastSuccessfulIndexJson, 1, $limit), substr(LastAttemptJson, 1, $limit) FROM IndexRunState WHERE Id = 1;";
        command.Parameters.AddWithValue("$limit", IndexRunSerialization.MaximumJsonLength + 1);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return new(null, null, []);
        }

        var diagnostics = new List<IndexDiagnostic>();
        var success = Parse(0);
        var attempt = Parse(1);
        if (success is { Outcome: not "succeeded" })
        {
            success = null;
            AddInvalidHistoryDiagnostic();
        }

        return new(success, attempt, diagnostics);

        IndexRunSummary? Parse(int column)
        {
            try
            {
                return IndexRunSerialization.Deserialize(reader.IsDBNull(column) ? null : reader.GetString(column));
            }
            catch (JsonException)
            {
                AddInvalidHistoryDiagnostic();

                return null;
            }
        }

        void AddInvalidHistoryDiagnostic()
        {
            if (diagnostics.Count == 0)
            {
                diagnostics.Add(new(
                    "index_history_unreadable",
                    "warning",
                    "Stored indexing history could not be read; graph counts remain available.",
                    Suggestion: "Run indexing again to refresh the bounded diagnostic history."));
            }
        }
    }

    private static async Task<HashSet<string>> ReadStrings(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var values = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private sealed record Counts(long GraphNodes, long CodeNodes, long Edges, long Files, long Projects, long EmbeddedNodes, long Memories);
}
