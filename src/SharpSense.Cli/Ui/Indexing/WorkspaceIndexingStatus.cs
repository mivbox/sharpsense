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
    long Revision);

public sealed record StartWorkspaceIndexingRequest(bool Watch = false, bool SkipEmbeddings = false);

internal sealed record WorkspaceIndexingUpdate(
    string State,
    string Message,
    int? CompletedItems = null,
    int? TotalItems = null,
    IReadOnlyList<string>? Diagnostics = null,
    bool IndexCommitted = false);
