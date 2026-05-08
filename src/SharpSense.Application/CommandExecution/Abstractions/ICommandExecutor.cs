using FluentResults;
using SharpSense.Application.CommandExecution.Models;

namespace SharpSense.Application.CommandExecution.Abstractions;

/// <summary>
/// Runs a caller-supplied local command inside the current repository workspace, captures the streamed output, and
/// reduces the transcript into deterministic excerpts so CLI and MCP routes share the same execution contract.
/// </summary>
public interface ICommandExecutor
{
    /// <summary>
    /// Executes the command and returns either merged log excerpts or a compact summary when no query is supplied or no
    /// matches are found.
    /// </summary>
    /// <param name="request">The raw command string plus the reduction settings for the current execution.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<Result<CommandExecutionResult>> Execute(
        CommandExecutionRequest request,
        CancellationToken ct);
}
