using SharpSense.Application.Indexing;

namespace SharpSense.Infrastructure.Storage;

/// <summary>Source removal results computed under the workspace write lock.</summary>
public sealed record WorkspaceSourceRemoval(
    WorkspaceSelection Selection,
    IReadOnlyList<WorkspaceSource> RemovedSources,
    IReadOnlyList<WorkspaceSource> UnmatchedSources);
