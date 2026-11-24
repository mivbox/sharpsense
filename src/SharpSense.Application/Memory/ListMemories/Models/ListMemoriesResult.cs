namespace SharpSense.Application.Memory.ListMemories.Models;

/// <summary>
/// Paged result of workspace memories matching a <see cref="ListMemoriesQuery"/>, including aggregate totals for the
/// matched filter scope.
/// </summary>
public sealed record ListMemoriesResult(
    MemoryNodeSummary[] Items,
    int TotalCount,
    int StaleCount,
    bool HasMore);
