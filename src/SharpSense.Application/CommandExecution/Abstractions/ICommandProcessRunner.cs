using FluentResults;
using SharpSense.Application.CommandExecution.Models;

namespace SharpSense.Application.CommandExecution.Abstractions;

/// <summary>
/// Owns the host boundary for launching a local process without shell indirection and streaming bounded output
/// back to the Application layer while preserving the command exit code.
/// </summary>
public interface ICommandProcessRunner
{
    /// <summary>
    /// Starts the process described by the request, forwards every captured output line to the supplied callback, and
    /// returns the final exit code, observed line count and truncation state when the process completes.
    /// Excess output is drained without invoking the callback.
    /// </summary>
    /// <param name="request">The command, working directory and shared stdout/stderr capture limits.</param>
    /// <param name="onOutput">Callback invoked once per captured output line.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<Result<CommandProcessResult>> Execute(
        CommandProcessRequest request,
        Func<string, CancellationToken, Task> onOutput,
        CancellationToken ct);
}
