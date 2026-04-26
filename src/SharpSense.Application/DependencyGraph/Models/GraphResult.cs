namespace SharpSense.Application.DependencyGraph.Models;

public sealed record GraphResult(
    GraphNode[] Nodes,
    GraphEdge[] Edges);
