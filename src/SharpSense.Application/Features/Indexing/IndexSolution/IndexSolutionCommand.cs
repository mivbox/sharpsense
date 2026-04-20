using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Features.Indexing.IndexSolution;

public sealed record IndexSolutionCommand(
    string SolutionPath = "",
    bool IncludeEmbeddings = true,
    IProgress<IndexingProgress>? Progress = null,
    IProgress<EmbeddingGenerationProgress>? EmbeddingProgress = null);
