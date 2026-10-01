namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphNodeConnectionsRequest(
    int NodeId,
    int PageSize = 100,
    string? Cursor = null,
    string? Revision = null,
    bool IncludeTotal = false);
