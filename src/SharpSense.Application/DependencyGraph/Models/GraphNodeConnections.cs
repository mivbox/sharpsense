namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphNodeConnectionsRequest(
    int NodeId,
    int PageSize = 100,
    string? Cursor = null,
    string? Revision = null,
    bool IncludeTotal = false);

public sealed record GraphConnectionNode(
    int Id,
    string Label,
    string Type,
    string? RelativePath,
    int? ProjectId,
    int? CodeNodeId);

public sealed record GraphNodeRelationship(string Type, string Direction, string? Metadata);

public sealed record GraphNodeConnection(
    GraphConnectionNode Node,
    IReadOnlyList<GraphNodeRelationship> Relationships);

public sealed record GraphNodeConnectionsPage(
    GraphConnectionNode Node,
    string Revision,
    IReadOnlyList<GraphNodeConnection> Items,
    string? NextCursor,
    int? TotalCount);
