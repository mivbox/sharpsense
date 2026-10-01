namespace SharpSense.Application.Indexing.Notifications;

public sealed record AnalysisSourceStatus(
    WorkspaceSourceKind Kind,
    string Path,
    string State,
    int? CompletedItems,
    int? TotalItems,
    string? Message);
