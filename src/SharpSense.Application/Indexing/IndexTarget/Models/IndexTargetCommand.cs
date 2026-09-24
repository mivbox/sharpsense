using SharpSense.Application.Shared.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.Notifications;

namespace SharpSense.Application.Indexing.IndexTarget.Models;

public sealed record IndexTargetCommand(
    IProgress<IndexingProgress>? Progress = null,
    IProgress<EmbeddingGenerationProgress>? EmbeddingProgress = null,
    IReadOnlyList<WorkspaceFileChange>? ChangedFiles = null,
    IAnalysisNotifier? Notifier = null);
