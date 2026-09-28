using SharpSense.Application.WorkspaceExplorer.Models;

namespace SharpSense.Application.WorkspaceExplorer.Abstractions;

/// <summary>Reads the persisted counts displayed in the selected workspace overview.</summary>
public interface IWorkspaceOverviewReader
{
    Task<WorkspaceOverviewCounts> Read(CancellationToken ct);
}
