namespace SharpSense.Application.DependencyGraph.GetDependencyGraph.Models;

public sealed record GetDependencyGraphQuery(
    IReadOnlyList<int> DirectoryIds,
    bool IncludeBoundaryNodes = true);
