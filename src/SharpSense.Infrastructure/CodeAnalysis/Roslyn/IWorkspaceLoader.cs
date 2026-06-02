using Microsoft.CodeAnalysis;
using SharpSense.Application.Indexing.Models;
using Microsoft.CodeAnalysis.Text;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

/// <summary>
/// Owns loading, caching, and refreshing Roslyn workspaces for disk-backed targets without coupling higher-level analysis
/// code to MSBuild-specific workspace management details.
/// </summary>
public interface IWorkspaceLoader : IDisposable
{
    /// <summary>
    /// Loads the supplied target into a cached Roslyn workspace and returns the current solution snapshot plus any loader
    /// diagnostics raised while opening the target.
    /// </summary>
    /// <param name="targetPath">The target path to load.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current load operation.</param>
    /// <returns>The current workspace load result for the supplied target.</returns>
    Task<WorkspaceLoadResult> Load(
        string targetPath,
        CancellationToken ct = default);

    /// <summary>
    /// Applies the supplied file changes to an already-loaded workspace, reloading the workspace when incremental document
    /// updates are no longer safe, and returns the updated solution snapshot plus any loader diagnostics.
    /// </summary>
    /// <param name="targetPath">The target path whose workspace should be refreshed.</param>
    /// <param name="changedFiles">The file changes to apply.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current update operation.</param>
    /// <returns>The updated workspace load result.</returns>
    Task<WorkspaceLoadResult> UpdateDocuments(
        string targetPath,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        CancellationToken ct = default);
}

public sealed class WorkspaceLoadResult
{
    public WorkspaceLoadResult(
        Solution solution,
        IReadOnlyList<string> diagnostics)
    {
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public Solution Solution { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

public sealed class WorkspaceTextChange
{
    private WorkspaceTextChange(
        bool success,
        SourceText? updatedText,
        string errorMessage)
    {
        Success = success;
        UpdatedText = updatedText;
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
    }

    public bool Success { get; }

    public SourceText? UpdatedText { get; }

    public string ErrorMessage { get; }
}

public sealed class WorkspaceTextUpdateResult
{
    public WorkspaceTextUpdateResult(
        bool success,
        IReadOnlyList<string> diagnostics,
        string errorMessage)
    {
        Success = success;
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
    }

    public bool Success { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public string ErrorMessage { get; }
}
