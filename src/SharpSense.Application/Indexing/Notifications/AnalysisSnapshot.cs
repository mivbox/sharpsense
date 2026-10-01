namespace SharpSense.Application.Indexing.Notifications;

/// <summary>An immutable view for terminal and browser presenters, independent of either transport.</summary>
public sealed record AnalysisSnapshot(
    Guid OperationId,
    long Sequence,
    AnalysisOperationKind OperationKind,
    string State,
    AnalysisPhase? Phase,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? Message,
    int? CompletedItems,
    int? TotalItems,
    IReadOnlyList<AnalysisSourceStatus> Sources,
    IReadOnlyList<string> Diagnostics,
    AnalysisSummary? Summary,
    AnalysisSummary? LastCommittedSummary);
