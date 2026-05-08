using FluentResults;
using SharpSense.Application.CommandExecution.Models;

namespace SharpSense.Application.CommandExecution.Abstractions;

/// <summary>
/// Represents a per-execution transient text index that assigns stable line numbers as output arrives, resolves matching
/// line ids for a search query, and rehydrates requested line ranges without persisting any transcript to the repository
/// database.
/// </summary>
public interface IExecutionLogIndex : IAsyncDisposable
{
    /// <summary>
    /// Appends one captured output line to the transient index and returns the assigned line number.
    /// </summary>
    /// <param name="line">The raw captured line text.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<Result<int>> AppendLine(
        string line,
        CancellationToken ct);

    /// <summary>
    /// Resolves the line numbers that match the supplied full-text query.
    /// </summary>
    /// <param name="query">The full-text query used to locate relevant lines.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<Result<int[]>> FindMatches(
        string query,
        CancellationToken ct);

    /// <summary>
    /// Rehydrates the lines inside the supplied inclusive line-number range.
    /// </summary>
    /// <param name="range">The inclusive range of lines to fetch from the transient index.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<Result<ExecutionLogLine[]>> ReadRange(
        ExecutionLineRange range,
        CancellationToken ct);
}
