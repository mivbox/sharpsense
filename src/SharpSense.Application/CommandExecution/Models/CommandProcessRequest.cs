namespace SharpSense.Application.CommandExecution.Models;

/// <summary>Limits apply jointly to stdout and stderr; bytes count UTF-8 line text without terminators.</summary>
public sealed record CommandProcessRequest(
    string Command,
    string WorkingDirectory,
    int MaxCapturedLines = 5000,
    int MaxCapturedBytes = 1_048_576);
