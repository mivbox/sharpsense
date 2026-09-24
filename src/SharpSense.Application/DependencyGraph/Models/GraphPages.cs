namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphPageRequest(
    IReadOnlyList<int> DirectoryIds,
    int PageSize = 2_000,
    string? Cursor = null,
    string? Revision = null,
    bool IncludeTotal = false);

public sealed record GraphPageNode(
    int Id,
    string Label,
    string Type,
    string? RelativePath,
    int? ProjectId,
    string Scope,
    bool IsClickable,
    int? CodeNodeId = null);

public sealed record GraphPageEdge(
    int Source,
    int Target,
    string Type,
    string Scope,
    string? Metadata = null);

public sealed record GraphNodesPage(
    string Revision,
    IReadOnlyList<GraphPageNode> Items,
    string? NextCursor,
    int? TotalCount);

public sealed record GraphEdgesPage(
    string Revision,
    IReadOnlyList<GraphPageEdge> Items,
    string? NextCursor,
    int? TotalCount);

public sealed class GraphRevisionChangedException()
    : InvalidOperationException("The workspace graph changed while loading. Restart graph loading to use the latest index.");
