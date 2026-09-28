namespace SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;

public sealed record UpdateWorkspaceFilesOutcome(
    int ProjectsReindexed,
    int CodeNodesPersisted,
    int DependencyEdgesPersisted,
    bool IndexCommitted = true);
