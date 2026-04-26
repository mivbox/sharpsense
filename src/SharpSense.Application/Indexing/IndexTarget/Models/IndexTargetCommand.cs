using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Indexing.IndexTarget.Models;

public sealed record IndexTargetCommand(
    IProgress<IndexingProgress>? Progress = null,
    IProgress<EmbeddingGenerationProgress>? EmbeddingProgress = null);
