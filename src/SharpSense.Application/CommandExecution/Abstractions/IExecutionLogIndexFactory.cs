namespace SharpSense.Application.CommandExecution.Abstractions;

/// <summary>
/// Creates a fresh transient execution-log index for each command invocation so output capture never shares state across
/// separate CLI or MCP requests.
/// </summary>
public interface IExecutionLogIndexFactory
{
    /// <summary>
    /// Creates a new transient execution-log index for the current command invocation.
    /// </summary>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<IExecutionLogIndex> Create(CancellationToken ct);
}
