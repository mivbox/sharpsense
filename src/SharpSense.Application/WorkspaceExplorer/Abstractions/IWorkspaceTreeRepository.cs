using SharpSense.Application.WorkspaceExplorer.Models;

namespace SharpSense.Application.WorkspaceExplorer.Abstractions;

/// <summary>
/// Reads the persisted workspace-tree hierarchy produced during analysis so the UI can lazily browse analyzed scope
/// without walking the repository filesystem at runtime.
/// </summary>
public interface IWorkspaceTreeRepository
{
    /// <summary>
    /// Returns the immediate children for the supplied persisted tree path. The special path value <c>/</c> returns
    /// the top-level rows of the analyzed hierarchy.
    /// </summary>
    /// <param name="path">The persisted tree path whose immediate children should be returned.</param>
    /// <param name="ct"><see cref="CancellationToken" /> for the current request.</param>
    Task<WorkspaceTreeResult> GetTree(
        string path,
        CancellationToken ct);
}
