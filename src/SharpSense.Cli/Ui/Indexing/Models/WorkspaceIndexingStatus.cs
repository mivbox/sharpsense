using SharpSense.Application.Indexing.Notifications;

namespace SharpSense.Cli.Ui.Indexing;

public sealed record WorkspaceIndexingStatus(
    Guid WorkspaceId,
    Guid? JobId,
    string State,
    bool Watch,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Message,
    int? CompletedItems,
    int? TotalItems,
    IReadOnlyList<string> Diagnostics,
    long Revision,
    long Sequence = 0,
    Guid? StreamId = null,
    DateTimeOffset? UpdatedAt = null,
    AnalysisSnapshot? Analysis = null);
