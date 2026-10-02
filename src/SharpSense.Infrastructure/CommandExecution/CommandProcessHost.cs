namespace SharpSense.Infrastructure.CommandExecution;

/// <summary>The host executable and arguments used to launch an isolated command supervisor.</summary>
public sealed record CommandProcessHost(string Executable, IReadOnlyList<string> Arguments);
