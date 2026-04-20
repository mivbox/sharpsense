namespace SharpSense.Application.Features.DependencyGraph.Contracts;

public sealed record GraphEdge(
    string Id,
    string Source,
    string Target,
    string Type);
