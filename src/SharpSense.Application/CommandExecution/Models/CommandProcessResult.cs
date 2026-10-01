namespace SharpSense.Application.CommandExecution.Models;

/// <summary>Reports all observed lines, including output drained after capture stopped.</summary>
public sealed record CommandProcessResult(int ExitCode, int TotalLines, bool CaptureTruncated = false);
