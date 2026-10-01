namespace SharpSense.Application.GraphStats.Models;

public sealed record IndexRunSummary(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double DurationMs,
    string Outcome,
    string Kind,
    string Scope,
    long? ExtractedNodeCount = null,
    long? ReusedEmbeddingCount = null,
    long? GeneratedEmbeddingCount = null,
    IReadOnlyList<IndexPhaseTiming>? Phases = null,
    IReadOnlyList<IndexDiagnostic>? Diagnostics = null);
