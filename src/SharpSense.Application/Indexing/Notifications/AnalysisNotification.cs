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
