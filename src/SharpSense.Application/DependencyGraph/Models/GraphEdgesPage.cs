namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphEdgesPage(
    string Revision,
    IReadOnlyList<GraphPageEdge> Items,
    string? NextCursor,
    int? TotalCount);
