namespace SharpSense.Application.DependencyGraph.GetDependencyGraphEdges.Models;

public sealed record GetDependencyGraphEdgesQuery(
    IReadOnlyList<int> DirectoryIds);
