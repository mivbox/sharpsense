namespace SharpSense.Application.CommandExecution.Models;

public sealed record CommandExecutionBlock(
    int StartLine,
    int EndLine,
    string Text);
