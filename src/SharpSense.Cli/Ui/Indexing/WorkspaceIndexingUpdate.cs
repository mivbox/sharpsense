using SharpSense.Application.Indexing.Notifications;

namespace SharpSense.Cli.Ui.Indexing;

internal sealed record WorkspaceIndexingUpdate(
    string State,
    string Message,
    int? CompletedItems = null,
    int? TotalItems = null,
    IReadOnlyList<string>? Diagnostics = null,
    bool IndexCommitted = false,
    AnalysisSnapshot? Analysis = null);
