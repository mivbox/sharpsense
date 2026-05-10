namespace SharpSense.Application.CommandExecution.Models;

public sealed record ExecutionLogLine(
    int LineNumber,
    string Text);
