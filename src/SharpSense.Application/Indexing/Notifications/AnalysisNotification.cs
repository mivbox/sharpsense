namespace SharpSense.Application.Indexing.Notifications;

public sealed record AnalysisNotification(
    Guid OperationId,
    long Sequence,
    DateTimeOffset Timestamp,
    AnalysisNotificationKind Kind,
    AnalysisOperationKind OperationKind,
    AnalysisPhase? Phase = null,
    AnalysisSource? Source = null,
    string? Message = null,
    int? CompletedItems = null,
    int? TotalItems = null,
    AnalysisSummary? Summary = null);

public enum AnalysisNotificationKind
{
    Started,
    PhaseChanged,
    SourceStarted,
    SourceProgress,
    SourceCompleted,
    SourceReused,
    EmbeddingProgress,
    Diagnostic,
    Committed,
    Ignored,
    Failed,
    Cancelled
}

public enum AnalysisOperationKind
{
    Full,
    Incremental
}

public enum AnalysisPhase
{
    Discovery,
    Extraction,
    Embeddings,
    Persistence
}

public sealed record AnalysisSource(WorkspaceSourceKind Kind, string Path);

/// <summary>
/// Counts describe the committed payload. For a legacy file update they describe the
/// replacement scope, rather than claiming to count the complete stored graph.
/// </summary>
public sealed record AnalysisSummary(
    int Projects,
    int Nodes,
    int Edges,
    int Documents,
    int ExtractedSources,
    int ReusedSources,
    int ReusedEmbeddings,
    int GeneratedEmbeddings,
    int DiagnosticCount);
