namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphPageEdge(
    int Source,
    int Target,
    string Type,
    string Scope,
    string? Metadata = null);
