using FluentResults;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.ExecuteProcess.Models;
using SharpSense.Application.CommandExecution.Models;
using System.Text;

namespace SharpSense.Application.CommandExecution.ExecuteProcess;

internal sealed class CommandOutputReader(IExecuteLogIndex executeLogIndex)
{
    public async Task<Result<CommandExecutionResult>> Read(
        ExecuteProcessCommand request,
        CommandProcessRequest processRequest,
        CommandProcessResult processResult,
        int indexedLineCount,
        CancellationToken ct)
    {
        var command = processRequest.Command;
        var workingDirectory = processRequest.WorkingDirectory;
        var totalObservedLines = processResult.TotalLines;
        var captureLimitReached = processResult.CaptureTruncated;
        var query = NormalizeQuery(request.Query);
        if (query is null)
        {
            return Result.Ok(CreateSummaryOnlyResult(
                command,
                workingDirectory,
                query,
                processResult.ExitCode,
                totalObservedLines,
                "No query was provided.",
                captureLimitReached,
                request.MaxCapturedLines,
                request.MaxCapturedBytes));
        }

        var matchResult = await executeLogIndex.FindMatches(query, ct);
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
                processResult.ExitCode,
                totalObservedLines,
                "Query returned no matches.",
                captureLimitReached,
                request.MaxCapturedLines,
                request.MaxCapturedBytes));
        }

        var ranges = MergeRanges(matchResult.Value, indexedLineCount, request.ContextLineCount);
        var blockBuildResult = await BuildBlocks(
            executeLogIndex,
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
            request.MaxCapturedLines,
            request.MaxCapturedBytes);

        return Result.Ok(new CommandExecutionResult(
            command,
            workingDirectory,
            query,
            processResult.ExitCode,
            totalObservedLines,
            matchResult.Value.Length,
            blockBuildResult.Value.Truncated || captureLimitReached,
            blockSummary,
            [.. blockBuildResult.Value.Blocks]));
    }

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
        int maxCapturedLines,
        int maxCapturedBytes)
        => new(
            command,
            workingDirectory,
            query,
            exitCode,
            totalLines,
            0,
            captureLimitReached,
            BuildSummaryOnlyMessage(
                exitCode,
                totalLines,
                reason,
                captureLimitReached,
                maxCapturedLines,
                maxCapturedBytes),
            []);

    private static string BuildSummaryOnlyMessage(
        int exitCode,
        int totalLines,
        string reason,
        bool captureLimitReached,
        int maxCapturedLines,
        int maxCapturedBytes)
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
                .Append(" indexed line(s) or ")
                .Append(maxCapturedBytes)
                .Append(" UTF-8 bytes.");
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
        int maxCapturedLines,
        int maxCapturedBytes)
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
                .Append(" indexed line(s) or ")
                .Append(maxCapturedBytes)
                .Append(" UTF-8 bytes.");
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
        IExecuteLogIndex executeLogIndex,
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
            var linesResult = await executeLogIndex.ReadRange(range, ct);
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

        var suffix = maxCharacters > 3 ? "..." : string.Empty;
        var length = maxCharacters - suffix.Length;
        if (length > 0 && char.IsHighSurrogate(line[length - 1]) && char.IsLowSurrogate(line[length]))
        {
            length--;
        }

        return line[..length] + suffix;
    }

}
