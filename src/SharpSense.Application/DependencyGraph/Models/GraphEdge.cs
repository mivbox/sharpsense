namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphEdge(
    string Id,
    string Source,
    string Target,
    string Type);
