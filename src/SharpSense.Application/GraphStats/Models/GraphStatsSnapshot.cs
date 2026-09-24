namespace SharpSense.Application.GraphStats.Models;

public sealed record GraphStatsSnapshot(
    string RepositoryRoot,
    string DatabasePath,
    string DatabaseState,
    bool IsIndexed,
    long GraphNodeCount,
    long CodeNodeCount,
    long EdgeCount,
    long FileCount,
    long ProjectCount,
    long EmbeddedNodeCount,
    long MemoryCount,
    IReadOnlyList<LanguageStats> Languages,
    IReadOnlyList<EdgeTypeStats> EdgeTypes,
    IndexRunSummary? LastSuccessfulIndex,
    IndexRunSummary? LastAttempt,
    IReadOnlyList<IndexDiagnostic> Diagnostics,
    Guid? WorkspaceId = null,
    string? WorkspaceName = null);

public sealed record LanguageStats(
    string Language,
    long FileCount,
    long NodeCount,
    long EmbeddedNodeCount);

public sealed record EdgeTypeStats(string EdgeType, long Count);

public sealed record IndexDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? FilePath = null,
    string? Suggestion = null);

public sealed record IndexPhaseTiming(string Name, double DurationMs);

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

public sealed record IndexRunHistory(
    IndexRunSummary? LastSuccessfulIndex,
    IndexRunSummary? LastAttempt,
    IReadOnlyList<IndexDiagnostic> Diagnostics);
