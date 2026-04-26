using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Features.Indexing.IndexTarget;

public sealed record IndexTargetCommand(
    IProgress<IndexingProgress>? Progress = null,
    IProgress<EmbeddingGenerationProgress>? EmbeddingProgress = null);
