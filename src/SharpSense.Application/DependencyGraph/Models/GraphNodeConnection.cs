namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphNodeConnection(
    GraphConnectionNode Node,
    IReadOnlyList<GraphNodeRelationship> Relationships);
