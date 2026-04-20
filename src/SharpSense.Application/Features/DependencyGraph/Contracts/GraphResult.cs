namespace SharpSense.Application.Features.DependencyGraph.Contracts;

public sealed record GraphResult(
    GraphNode[] Nodes,
    GraphEdge[] Edges);
