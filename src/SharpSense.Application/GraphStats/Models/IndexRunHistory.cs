namespace SharpSense.Application.GraphStats.Models;

public sealed record IndexRunHistory(
    IndexRunSummary? LastSuccessfulIndex,
    IndexRunSummary? LastAttempt,
    IReadOnlyList<IndexDiagnostic> Diagnostics);
