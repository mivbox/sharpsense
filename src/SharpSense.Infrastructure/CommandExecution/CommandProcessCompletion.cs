namespace SharpSense.Infrastructure.CommandExecution;

internal sealed record CommandProcessCompletion(int ExitCode, string? Error = null);
