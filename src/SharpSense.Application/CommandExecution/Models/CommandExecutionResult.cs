namespace SharpSense.Application.CommandExecution.Models;

public sealed record CommandExecutionResult(
    string Command,
    string WorkingDirectory,
    string? Query,
    int ExitCode,
    int TotalLines,
    int MatchedLineCount,
    bool Truncated,
    string Summary,
    CommandExecutionBlock[] Blocks)
{
    public bool Success => ExitCode == 0;

    public string Status => Success
        ? "success"
        : "failure";

    public int BlockCount => Blocks.Length;
}
