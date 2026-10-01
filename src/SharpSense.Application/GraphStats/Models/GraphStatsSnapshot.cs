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
