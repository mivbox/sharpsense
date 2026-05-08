using System.Text;
using FluentResults;
using Microsoft.Extensions.Options;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Shared.Options;

namespace SharpSense.Application.CommandExecution;

public sealed class CommandExecutor(
    ICommandProcessRunner processRunner,
    IExecutionLogIndexFactory executionLogIndexFactory,
    IOptions<SharpSenseCliOptions> cliOptions)
    : ICommandExecutor
{
    public async Task<Result<CommandExecutionResult>> Execute(
        CommandExecutionRequest request,
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

        var command = request.Command.Trim();
        var workingDirectory = ResolveWorkingDirectory();
        await using var executionLogIndex = await executionLogIndexFactory.Create(ct);
        var indexedLineCount = 0;
        var totalObservedLines = 0;
        var captureLimitReached = false;

        var processResult = await processRunner.Execute(
            new CommandProcessRequest(command, workingDirectory),
            async (line, innerCt) =>
            {
                totalObservedLines++;
                if (totalObservedLines > request.MaxCapturedLines)
                {
                    captureLimitReached = true;
                    return;
                }

                var appendResult = await executionLogIndex.AppendLine(line, innerCt);
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

        var query = NormalizeQuery(request.Query);
        if (query is null)
        {
            return Result.Ok(CreateSummaryOnlyResult(
                command,
                workingDirectory,
                query,
                processResult.Value.ExitCode,
                totalObservedLines,
                "No query was provided.",
                captureLimitReached,
                request.MaxCapturedLines));
        }

        var matchResult = await executionLogIndex.FindMatches(query, ct);
        if (matchResult.IsFailed)
        {
            return Result.Fail<CommandExecutionResult>(matchResult.Errors);
        }

        if (matchResult.Value.Length == 0)
        {
            return Result.Ok(CreateSummaryOnlyResult(
                command,
                workingDirectory,
                query,
                processResult.Value.ExitCode,
                totalObservedLines,
                "Query returned no matches.",
                captureLimitReached,
                request.MaxCapturedLines));
        }

        var ranges = MergeRanges(matchResult.Value, indexedLineCount, request.ContextLineCount);
        var blockBuildResult = await BuildBlocks(
            executionLogIndex,
            ranges,
            request.MaxCharacters,
            ct);
        if (blockBuildResult.IsFailed)
        {
            return Result.Fail<CommandExecutionResult>(blockBuildResult.Errors);
        }

        var blockSummary = BuildMatchedSummary(
            matchResult.Value.Length,
            blockBuildResult.Value.Blocks.Count,
            totalObservedLines,
            blockBuildResult.Value.Truncated,
            request.MaxCharacters,
            captureLimitReached,
            request.MaxCapturedLines);

        return Result.Ok(new CommandExecutionResult(
            command,
            workingDirectory,
            query,
            processResult.Value.ExitCode,
            totalObservedLines,
            matchResult.Value.Length,
            blockBuildResult.Value.Truncated || captureLimitReached,
            blockSummary,
            [.. blockBuildResult.Value.Blocks]));
    }

    private string ResolveWorkingDirectory()
        => string.IsNullOrWhiteSpace(cliOptions.Value.RepositoryRoot)
            ? Environment.CurrentDirectory
            : cliOptions.Value.RepositoryRoot;

    private static string? NormalizeQuery(string? query)
        => string.IsNullOrWhiteSpace(query)
            ? null
            : query.Trim();

    private static CommandExecutionResult CreateSummaryOnlyResult(
        string command,
        string workingDirectory,
        string? query,
        int exitCode,
        int totalLines,
        string reason,
        bool captureLimitReached,
        int maxCapturedLines)
        => new(
            command,
            workingDirectory,
            query,
            exitCode,
            totalLines,
            0,
            captureLimitReached,
            BuildSummaryOnlyMessage(exitCode, totalLines, reason, captureLimitReached, maxCapturedLines),
            []);

    private static string BuildSummaryOnlyMessage(
        int exitCode,
        int totalLines,
        string reason,
        bool captureLimitReached,
        int maxCapturedLines)
    {
        var builder = new StringBuilder()
            .Append("Command completed ")
            .Append(exitCode == 0 ? "successfully" : "with failures")
            .Append(" with ")
            .Append(totalLines)
            .Append(" captured line(s). ")
            .Append(reason);

        if (captureLimitReached)
        {
            builder.Append(" Output capture was capped at ")
                .Append(maxCapturedLines)
                .Append(" indexed line(s).");
        }

        return builder.ToString();
    }

    private static string BuildMatchedSummary(
        int matchedLineCount,
        int blockCount,
        int totalLines,
        bool truncated,
        int maxCharacters,
        bool captureLimitReached,
        int maxCapturedLines)
    {
        var builder = new StringBuilder()
            .Append("Returned ")
            .Append(blockCount)
            .Append(" merged block(s) from ")
            .Append(matchedLineCount)
            .Append(" matched line(s) across ")
            .Append(totalLines)
            .Append(" captured line(s).");

        if (truncated)
        {
            builder.Append(" Reduced output truncated at ")
                .Append(maxCharacters)
                .Append(" characters.");
        }

        if (captureLimitReached)
        {
            builder.Append(" Output capture was capped at ")
                .Append(maxCapturedLines)
                .Append(" indexed line(s).");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<ExecutionLineRange> MergeRanges(
        IReadOnlyList<int> matchedLines,
        int totalLines,
        int contextLineCount)
    {
        if (matchedLines.Count == 0)
        {
            return [];
        }

        var orderedMatches = matchedLines
            .Distinct()
            .OrderBy(static lineNumber => lineNumber)
            .ToArray();
        var merged = new List<ExecutionLineRange>();

        foreach (var matchedLine in orderedMatches)
        {
            var range = new ExecutionLineRange(
                Math.Max(1, matchedLine - contextLineCount),
                Math.Min(totalLines, matchedLine + contextLineCount));

            if (merged.Count == 0)
            {
                merged.Add(range);
                continue;
            }

            var previous = merged[^1];
            if (range.StartLine <= previous.EndLine + 1)
            {
                merged[^1] = new ExecutionLineRange(
                    previous.StartLine,
                    Math.Max(previous.EndLine, range.EndLine));
                continue;
            }

            merged.Add(range);
        }

        return merged;
    }

    private static async Task<Result<(List<CommandExecutionBlock> Blocks, bool Truncated)>> BuildBlocks(
        IExecutionLogIndex executionLogIndex,
        IReadOnlyList<ExecutionLineRange> ranges,
        int maxCharacters,
        CancellationToken ct)
    {
        var blocks = new List<CommandExecutionBlock>();
        var remainingCharacters = maxCharacters;
        var truncated = false;

        foreach (var range in ranges)
        {
            var separatorLength = blocks.Count == 0
                ? 0
                : Environment.NewLine.Length * 2;
            if (remainingCharacters <= separatorLength)
            {
                truncated = true;
                break;
            }

            remainingCharacters -= separatorLength;
            var linesResult = await executionLogIndex.ReadRange(range, ct);
            if (linesResult.IsFailed)
            {
                return Result.Fail<(List<CommandExecutionBlock> Blocks, bool Truncated)>(linesResult.Errors);
            }

            var blockResult = CreateBlock(linesResult.Value, remainingCharacters);
            if (blockResult.Block is null)
            {
                truncated = true;
                break;
            }

            blocks.Add(blockResult.Block);
            remainingCharacters -= blockResult.Block.Text.Length;

            if (blockResult.Truncated)
            {
                truncated = true;
                break;
            }
        }

        return Result.Ok((blocks, truncated));
    }

    private static (CommandExecutionBlock? Block, bool Truncated) CreateBlock(
        IReadOnlyList<ExecutionLogLine> lines,
        int maxCharacters)
    {
        if (lines.Count == 0 || maxCharacters <= 0)
        {
            return (null, true);
        }

        var builder = new StringBuilder();
        var startLine = 0;
        var endLine = 0;
        var truncated = false;

        foreach (var line in lines)
        {
            var renderedLine = $"{line.LineNumber}| {line.Text}";
            var separator = builder.Length == 0
                ? string.Empty
                : Environment.NewLine;
            var remainingCharacters = maxCharacters - builder.Length - separator.Length;
            if (remainingCharacters <= 0)
            {
                truncated = true;
                break;
            }

            if (renderedLine.Length > remainingCharacters)
            {
                if (builder.Length > 0)
                {
                    builder.Append(separator);
                }

                builder.Append(TruncateLine(renderedLine, remainingCharacters));
                startLine = startLine == 0
                    ? line.LineNumber
                    : startLine;
                endLine = line.LineNumber;
                truncated = true;
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append(separator);
            }

            builder.Append(renderedLine);
            startLine = startLine == 0
                ? line.LineNumber
                : startLine;
            endLine = line.LineNumber;
        }

        return builder.Length == 0
            ? (null, truncated)
            : (new CommandExecutionBlock(startLine, endLine, builder.ToString()), truncated);
    }

    private static string TruncateLine(string line, int maxCharacters)
    {
        if (line.Length <= maxCharacters)
        {
            return line;
        }

        if (maxCharacters <= 3)
        {
            return line[..maxCharacters];
        }

        return line[..(maxCharacters - 3)] + "...";
    }

    private static string GetErrorMessage(IEnumerable<IError> errors)
        => string.Join("; ", errors.Select(static error => error.Message));
}
