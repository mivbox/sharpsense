namespace SharpSense.Application.DependencyGraph.GetDependencyGraph.Models;

public sealed record GetDependencyGraphQuery(
    IReadOnlyList<string> Paths,
    bool IncludeBoundaryNodes = true);
