namespace SharpSense.Application.Indexing.IndexWorkspace.Models;

public sealed record IndexWorkspaceOutcome(
    int ProjectsIndexed,
    int CodeNodesPersisted,
    int DependencyEdgesPersisted,
    int DocumentNodesPersisted);
