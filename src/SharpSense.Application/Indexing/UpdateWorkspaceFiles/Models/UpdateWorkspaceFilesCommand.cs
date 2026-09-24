using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Indexing.Notifications;

namespace SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;

public sealed record UpdateWorkspaceFilesCommand(
    IReadOnlyList<WorkspaceFileChange>? ChangedFiles = null,
    IProgress<IndexingProgress>? Progress = null,
    IProgress<EmbeddingGenerationProgress>? EmbeddingProgress = null,
    IAnalysisNotifier? Notifier = null);
