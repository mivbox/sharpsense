using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Indexing.IndexWorkspace.Models;

public sealed record IndexWorkspaceCommand(
    IProgress<IndexingProgress>? Progress = null,
    IProgress<EmbeddingGenerationProgress>? EmbeddingProgress = null,
    IReadOnlyList<WorkspaceFileChange>? ChangedFiles = null,
    IAnalysisNotifier? Notifier = null);
