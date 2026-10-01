using FluentResults;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.Models;
using System.Data.Common;

namespace SharpSense.Infrastructure.CommandExecution;

internal sealed class TransientExecutionLogIndex(TransientExecutionLogDbContext dbContext) : IExecuteLogIndex
{
    public async Task<Result<int>> AppendLine(
        string line,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(line);

        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO ExecutionLog (LineText)
                 VALUES ({line})
                 """,
                ct);

            var rowId = await dbContext.Database
                .SqlQuery<long>(
                    $"""
                     SELECT last_insert_rowid() AS Value
                     """)
                .SingleAsync(ct);

            return Result.Ok(Convert.ToInt32(rowId, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (DbException ex)
        {
            return Result.Fail<int>($"Failed to append command output to the in-memory execution index: {ex.Message}");
        }
    }

    public async Task<Result<int[]>> FindMatches(
        string query,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        try
        {
            var matches = await dbContext.Database
                .SqlQuery<int>(
                    $"""
                     SELECT rowid AS Value
                     FROM ExecutionLog
                     WHERE ExecutionLog MATCH {query}
                     ORDER BY rowid
                     """)
                .ToArrayAsync(ct);

            return Result.Ok(matches);
        }
        catch (DbException ex)
        {
            return Result.Fail<int[]>($"Failed to evaluate execution-log query '{query}': {ex.Message}");
        }
    }

    public async Task<Result<ExecutionLogLine[]>> ReadRange(
        ExecutionLineRange range,
        CancellationToken ct)
    {
        try
        {
            var lines = await dbContext.Database
                .SqlQuery<ExecutionLogLineRow>(
                    $"""
                     SELECT rowid AS LineNumber, LineText AS Text
                     FROM ExecutionLog
                     WHERE rowid BETWEEN {range.StartLine} AND {range.EndLine}
                     ORDER BY rowid
                     """)
                .ToArrayAsync(ct);

            return Result.Ok<ExecutionLogLine[]>(
                [
                    .. lines
                        .Select(static line => new ExecutionLogLine(
                            line.LineNumber,
                            line.Text))
                ]);
        }
        catch (DbException ex)
        {
            return Result.Fail<ExecutionLogLine[]>($"Failed to read execution-log range {range.StartLine}-{range.EndLine}: {ex.Message}");
        }
    }

    public ValueTask DisposeAsync()
        => dbContext.DisposeAsync();

    private sealed class ExecutionLogLineRow
    {
        public int LineNumber { get; init; }

        public string Text { get; init; } = string.Empty;
    }
}
