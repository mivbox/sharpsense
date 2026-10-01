using FluentResults;
using Microsoft.Extensions.Options;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.ExecuteProcess.Models;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;

namespace SharpSense.Application.CommandExecution.ExecuteProcess;

internal sealed class ExecuteProcessCommandHandler(
    ICommandProcessRunner processRunner,
    IExecuteLogIndexFactory executeLogIndexFactory,
    IOptions<WorkspaceExecutionOptions> workspaceOptions)
    : ICommandHandler<ExecuteProcessCommand, Result<CommandExecutionResult>>
{
    public async Task<Result<CommandExecutionResult>> Handle(
        ExecuteProcessCommand request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return Result.Fail<CommandExecutionResult>("Command must not be empty.");
        }

        if (request.ContextLineCount < 0)
        {
            return Result.Fail<CommandExecutionResult>("Context line count must be zero or greater.");
        }

        if (request.MaxCharacters <= 0)
        {
            return Result.Fail<CommandExecutionResult>("Max characters must be greater than zero.");
        }

        if (request.MaxCapturedLines <= 0)
        {
            return Result.Fail<CommandExecutionResult>("Max captured lines must be greater than zero.");
        }

        if (request.MaxCapturedBytes <= 0)
        {
            return Result.Fail<CommandExecutionResult>("Max captured bytes must be greater than zero.");
        }

        var command = request.Command.Trim();
        var workingDirectory = ResolveWorkingDirectory(workspaceOptions.Value.RepositoryRoot);
        var processRequest = new CommandProcessRequest(
            command,
            workingDirectory,
            request.MaxCapturedLines,
            request.MaxCapturedBytes);
        await using var executeLogIndex = await executeLogIndexFactory.Create(ct);
        var indexedLineCount = 0;

        var processResult = await processRunner.Execute(
            processRequest,
            async (line, innerCt) =>
            {
                var appendResult = await executeLogIndex.AppendLine(line, innerCt);
                if (appendResult.IsFailed)
                {
                    throw new InvalidOperationException(GetErrorMessage(appendResult.Errors));
                }

                indexedLineCount = appendResult.Value;
            },
            ct);

        if (processResult.IsFailed)
        {
            return Result.Fail<CommandExecutionResult>(processResult.Errors);
        }

        var outputReader = new CommandOutputReader(executeLogIndex);

        return await outputReader.Read(request, processRequest, processResult.Value, indexedLineCount, ct);
    }

    private static string ResolveWorkingDirectory(string? repositoryRoot)
        => string.IsNullOrWhiteSpace(repositoryRoot)
            ? Environment.CurrentDirectory
            : repositoryRoot;

    private static string GetErrorMessage(IEnumerable<IError> errors)
        => string.Join("; ", errors.Select(static error => error.Message));
}
