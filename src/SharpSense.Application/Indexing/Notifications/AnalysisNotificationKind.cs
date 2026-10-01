namespace SharpSense.Application.Indexing.Notifications;

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
