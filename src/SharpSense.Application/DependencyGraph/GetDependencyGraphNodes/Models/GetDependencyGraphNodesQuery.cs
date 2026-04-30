namespace SharpSense.Application.DependencyGraph.GetDependencyGraphNodes.Models;

public sealed record GetDependencyGraphNodesQuery(
    IReadOnlyList<int> DirectoryIds);
