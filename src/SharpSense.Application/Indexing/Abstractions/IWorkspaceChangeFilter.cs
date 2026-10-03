using SharpSense.Application.Indexing.Models;

namespace SharpSense.Application.Indexing.Abstractions;

/// <summary>
/// Tracks selected sources and their discovered dependencies for workspace watch updates.
/// </summary>
public interface IWorkspaceChangeFilter
{
    bool IsRelevant(IReadOnlyList<WorkspaceFileChange> changes);

    /// <summary>Identifies compiler inputs that must invalidate the graph even when Git ignores them.</summary>
    bool IsRelevantCodeChange(WorkspaceFileChange change);

    void TrackSource(WorkspaceSource source, ExtractedNodes extractedNodes);
}
