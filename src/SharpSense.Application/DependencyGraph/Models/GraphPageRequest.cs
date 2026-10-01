namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphPageRequest(
    IReadOnlyList<int> DirectoryIds,
    int PageSize = 2_000,
    string? Cursor = null,
    string? Revision = null,
    bool IncludeTotal = false);
