namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphPageNode(
    int Id,
    string Label,
    string Type,
    string? RelativePath,
    int? ProjectId,
    string Scope,
    bool IsClickable,
    int? CodeNodeId = null);
