namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphNodesPage(
    string Revision,
    IReadOnlyList<GraphPageNode> Items,
    string? NextCursor,
    int? TotalCount);
