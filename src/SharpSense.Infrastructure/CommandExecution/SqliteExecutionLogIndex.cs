using FluentResults;
using Microsoft.Data.Sqlite;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.Models;

namespace SharpSense.Infrastructure.CommandExecution;

internal sealed class SqliteExecutionLogIndex(SqliteConnection connection) : IExecutionLogIndex
{
    public async Task<Result<int>> AppendLine(
        string line,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(line);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ExecutionLog (LineText)
            VALUES ($lineText);
            """;
        command.Parameters.AddWithValue("$lineText", line);

        try
        {
            await command.ExecuteNonQueryAsync(ct);

            await using var rowIdCommand = connection.CreateCommand();
            rowIdCommand.CommandText = "SELECT last_insert_rowid();";
            var rowId = await rowIdCommand.ExecuteScalarAsync(ct);
            return Result.Ok(Convert.ToInt32(rowId, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (SqliteException ex)
        {
            return Result.Fail<int>($"Failed to append command output to the transient index: {ex.Message}");
        }
    }

    public async Task<Result<int[]>> FindMatches(
        string query,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rowid
            FROM ExecutionLog
            WHERE ExecutionLog MATCH $query
            ORDER BY rowid;
            """;
        command.Parameters.AddWithValue("$query", query);

        var matches = new List<int>();

        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                matches.Add(reader.GetInt32(0));
            }

            return Result.Ok(matches.ToArray());
        }
        catch (SqliteException ex)
        {
            return Result.Fail<int[]>($"Failed to evaluate execution-log query '{query}': {ex.Message}");
        }
    }

    public async Task<Result<ExecutionLogLine[]>> ReadRange(
        ExecutionLineRange range,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rowid, LineText
            FROM ExecutionLog
            WHERE rowid BETWEEN $startLine AND $endLine
            ORDER BY rowid;
            """;
        command.Parameters.AddWithValue("$startLine", range.StartLine);
        command.Parameters.AddWithValue("$endLine", range.EndLine);

        var lines = new List<ExecutionLogLine>();

        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                lines.Add(new ExecutionLogLine(
                    reader.GetInt32(0),
                    reader.GetString(1)));
            }

            return Result.Ok(lines.ToArray());
        }
        catch (SqliteException ex)
        {
            return Result.Fail<ExecutionLogLine[]>($"Failed to read execution-log range {range.StartLine}-{range.EndLine}: {ex.Message}");
        }
    }

    public ValueTask DisposeAsync()
        => connection.DisposeAsync();
}
