namespace SharpSense.Application.CommandExecution.Models;

public sealed record CommandProcessRequest(
    string Command,
    string WorkingDirectory);
