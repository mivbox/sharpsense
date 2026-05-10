namespace SharpSense.Application.CommandExecution.Abstractions;

/// <summary>
/// Creates a fresh transient execution-log index for each command invocation so output capture never shares state across
/// separate CLI or MCP requests and never implies retained build artifacts.
/// </summary>
public interface IExecuteLogIndexFactory
{
    /// <summary>
    /// Creates a new transient execution-log index for the current command invocation. The current implementation uses a
    /// private in-memory full-text index so <c>execute</c> can search live output without touching the repository
    /// database or requiring a retained-run lifecycle.
    /// </summary>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<IExecuteLogIndex> Create(CancellationToken ct);
}
