using FluentResults;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Errors;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

/// <summary>
/// Owns loading, caching, and refreshing Roslyn workspaces for disk-backed targets without coupling higher-level analysis
/// code to MSBuild-specific workspace management details.
/// </summary>
internal interface IWorkspaceLoader : IDisposable
{
    /// <summary>
    /// Loads the supplied target into a cached Roslyn workspace and returns the resulting solution snapshot wrapped in
    /// a <see cref="Result{T}"/>. The result carries any loader diagnostics raised while opening the target.
    /// </summary>
    /// <param name="targetPath">The target path to load.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current load operation.</param>
    /// <returns>
    /// A successful result with a <see cref="WorkspaceLoadResult"/> when the workspace opened cleanly, or
    /// <see cref="Result.Fail(string)"/> with one or more <see cref="ServiceError"/> entries when MSBuild reported critical
    /// project-load failures or <c>OpenSolutionAsync</c> threw.
    /// </returns>
    Task<Result<WorkspaceLoadResult>> Load(
        string targetPath,
        CancellationToken ct = default);

    /// <summary>Reopens the target from disk, discarding cached project evaluation and document snapshots.</summary>
    Task<Result<WorkspaceLoadResult>> Reload(string targetPath, CancellationToken ct = default);

    /// <summary>
    /// Applies the supplied file changes to an already-loaded workspace, reloading the workspace when incremental document
    /// updates are no longer safe. Returns the updated solution snapshot wrapped in a <see cref="Result{T}"/> so any
    /// reload-time critical diagnostics propagate as <see cref="ServiceError"/> instances.
    /// </summary>
    /// <param name="targetPath">The target path whose workspace should be refreshed.</param>
    /// <param name="changedFiles">The file changes to apply.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current update operation.</param>
    /// <returns>
    /// A successful result on a clean update, or <see cref="Result.Fail(string)"/> when the underlying reload
    /// raised critical diagnostics.
    /// </returns>
    Task<Result<WorkspaceLoadResult>> UpdateDocuments(
        string targetPath,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        CancellationToken ct = default);
}
