namespace SharpSense.Application.Shared.Models;

public sealed record IndexingProgress(
    string CurrentTask,
    int CompletedItems,
    int TotalItems);
