namespace SharpSense.Application.GraphStats.Models;

public sealed record IndexDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? FilePath = null,
    string? Suggestion = null);
