namespace SharpSense.Application.CommandExecution.ExecuteProcess.Models;

/// <summary>Runs a process and selects bounded output context in the current workspace.</summary>
public sealed record ExecuteProcessCommand(
    string Command,
    string? Query,
    int ContextLineCount = 3,
    int MaxCharacters = 4000,
    int MaxCapturedLines = 5000,
    int MaxCapturedBytes = 1_048_576);
