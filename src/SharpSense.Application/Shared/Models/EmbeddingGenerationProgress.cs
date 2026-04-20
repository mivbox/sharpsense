namespace SharpSense.Application.Shared.Models;

public sealed record EmbeddingGenerationProgress(
    string CurrentTask,
    int CompletedItems,
    int TotalItems);
