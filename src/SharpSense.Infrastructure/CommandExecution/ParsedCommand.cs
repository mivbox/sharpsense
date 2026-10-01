namespace SharpSense.Infrastructure.CommandExecution;

internal sealed record ParsedCommand(
    string Executable,
    string[] Arguments);
