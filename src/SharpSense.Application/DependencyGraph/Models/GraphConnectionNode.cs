namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphConnectionNode(
    int Id,
    string Label,
    string Type,
    string? RelativePath,
    int? ProjectId,
    int? CodeNodeId);
