namespace SharpSense.Application.CommandExecution.Models;

public sealed record CommandExecutionRequest(
    string Command,
    string? Query,
    int ContextLineCount = 3,
    int MaxCharacters = 4000,
    int MaxCapturedLines = 5000)
{
    public const int DefaultContextLineCount = 3;
    public const int DefaultMaxCharacters = 4000;
    public const int DefaultMaxCapturedLines = 5000;
}
