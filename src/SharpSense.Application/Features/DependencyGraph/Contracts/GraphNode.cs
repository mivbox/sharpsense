namespace SharpSense.Application.Features.DependencyGraph.Contracts;

public sealed record GraphNode(
    string Id,
    string Label,
    string Type);
