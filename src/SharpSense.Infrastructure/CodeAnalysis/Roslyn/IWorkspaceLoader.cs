using FluentResults;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Errors;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

/// <summary>
/// Owns loading, caching, and refreshing Roslyn workspaces for disk-backed targets without coupling higher-level analysis
/// code to MSBuild-specific workspace management details.
/// </summary>
public interface IWorkspaceLoader : IDisposable
{
    /// <summary>
    /// Loads the supplied target into a cached Roslyn workspace and returns the resulting solution snapshot wrapped in
    /// a <see cref="Result{T}"/>. The result carries any loader diagnostics raised while opening the target.
    /// </summary>
    /// <param name="targetPath">The target path to load.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current load operation.</param>
    /// <returns>
    /// <see cref="Result.Ok(T)"/> with a <see cref="WorkspaceLoadResult"/> when the workspace opened cleanly, or
    /// <see cref="Result.Fail(string)"/> with one or more <see cref="ServiceError"/> entries when MSBuild reported critical
    /// project-load failures or <c>OpenSolutionAsync</c> threw.
    /// </returns>
    Task<Result<WorkspaceLoadResult>> Load(
        string targetPath,
        CancellationToken ct = default);

    /// <summary>Reopens the target from disk, discarding cached project evaluation and document snapshots.</summary>
    Task<Result<WorkspaceLoadResult>> Reload(string targetPath, CancellationToken ct = default);

    Task<Result<WorkspaceLoadResult>> UpdateDocuments(
        string targetPath,
        IReadOnlyList<WorkspaceFileChange> changedFiles)
        => UpdateDocuments(
            targetPath,
            changedFiles,
            CancellationToken.None);

    /// <summary>
    /// Applies the supplied file changes to an already-loaded workspace, reloading the workspace when incremental document
    /// updates are no longer safe. Returns the updated solution snapshot wrapped in a <see cref="Result{T}"/> so any
    /// reload-time critical diagnostics propagate as <see cref="ServiceError"/> instances.
    /// </summary>
    /// <param name="targetPath">The target path whose workspace should be refreshed.</param>
    /// <param name="changedFiles">The file changes to apply.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current update operation.</param>
    /// <returns>
    /// <see cref="Result.Ok(T)"/> on a clean update, or <see cref="Result.Fail(string)"/> when the underlying reload
    /// raised critical diagnostics.
    /// </returns>
    Task<Result<WorkspaceLoadResult>> UpdateDocuments(
        string targetPath,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        CancellationToken ct = default);

    Task<WorkspaceTextUpdateResult> ChangeDocumentText(
        string targetPath,
        string documentPath,
        Func<SourceText, WorkspaceTextChange> changeText)
        => ChangeDocumentText(
            targetPath,
            documentPath,
            changeText,
            CancellationToken.None);

    /// <summary>
    /// Applies a text mutation to every workspace document that maps to the supplied physical file path while holding the
    /// cached workspace gate, then persists the Roslyn changes through the backing workspace.
    /// </summary>
    /// <param name="targetPath">The target path whose workspace should be mutated.</param>
    /// <param name="documentPath">The physical file path of the document to mutate.</param>
    /// <param name="changeText">The callback that produces the updated text from the current source text.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current mutation.</param>
    /// <returns>The result of the text mutation.</returns>
    Task<WorkspaceTextUpdateResult> ChangeDocumentText(
        string targetPath,
        string documentPath,
        Func<SourceText, WorkspaceTextChange> changeText,
        CancellationToken ct = default);
}

public sealed class WorkspaceLoadResult
{
    public WorkspaceLoadResult(
        Solution solution,
        IReadOnlyList<string> diagnostics)
        : this(solution, diagnostics, [])
    {
    }

    public WorkspaceLoadResult(
        Solution solution,
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<ServiceError> errors)
    {
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    public Solution Solution { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public IReadOnlyList<ServiceError> Errors { get; }

    public IReadOnlyList<Project> OrderedProjects =>
    [
        .. Solution.Projects
            .OrderBy(static project => project.FilePath ?? project.Name, StringComparer.Ordinal)
    ];
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

    public static WorkspaceTextChange SuccessChange(SourceText updatedText)
        => new(
            true,
            updatedText ?? throw new ArgumentNullException(nameof(updatedText)),
            string.Empty);

    public static WorkspaceTextChange Failure(string errorMessage)
        => new(
            false,
            null,
            errorMessage);
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
