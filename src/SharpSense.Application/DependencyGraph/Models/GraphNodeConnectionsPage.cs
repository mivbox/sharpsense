namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphNodeConnectionsPage(
    GraphConnectionNode Node,
    string Revision,
    IReadOnlyList<GraphNodeConnection> Items,
    string? NextCursor,
    int? TotalCount);
