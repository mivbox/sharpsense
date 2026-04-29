namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphNode(
    string Id,
    string Label,
    string Type,
    string? RelativePath,
    string? ProjectId,
    string Scope,
    bool IsClickable);
