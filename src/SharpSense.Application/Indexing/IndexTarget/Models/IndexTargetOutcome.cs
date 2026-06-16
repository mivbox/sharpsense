namespace SharpSense.Application.Indexing.IndexTarget.Models;

public sealed record IndexTargetOutcome(
    int ProjectsIndexed,
    int CodeNodesPersisted,
    int DependencyEdgesPersisted,
    int DocumentNodesPersisted);
