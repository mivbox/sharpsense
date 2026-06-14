using Microsoft.CodeAnalysis;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

/// <summary>
/// Transforms already-loaded Roslyn targets into project nodes, code nodes, and dependency edges without owning the
/// physical workspace-loading concern. Implementations operate on in-memory <see cref="Solution"/> or <see cref="Project"/>
/// models so higher layers can test analysis independently from MSBuild and disk-backed workspaces.
/// </summary>
public interface ITargetAnalysisEngine
{
    Task<KnowledgeGraphExtractionPayload> Extract(
        string targetPath,
        Solution solution,
        IRepositoryWorkspace repositoryWorkspace)
        => Extract(
            targetPath,
            solution,
            repositoryWorkspace,
            progress: null,
            diagnostics: null,
            CancellationToken.None);

    Task<KnowledgeGraphExtractionPayload> Extract(
        string targetPath,
        Solution solution,
        IRepositoryWorkspace repositoryWorkspace,
        CancellationToken ct)
        => Extract(
            targetPath,
            solution,
            repositoryWorkspace,
            progress: null,
            diagnostics: null,
            ct);

    /// <summary>
    /// Extracts a full knowledge-graph payload from the supplied Roslyn solution.
    /// </summary>
    /// <param name="targetPath">The canonical target path being analyzed.</param>
    /// <param name="solution">The loaded Roslyn solution to analyze.</param>
    /// <param name="repositoryWorkspace">The repository workspace used for path normalization.</param>
    /// <param name="progress">Optional progress reporter for the current analysis session.</param>
    /// <param name="diagnostics">Optional diagnostics collected during workspace loading.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current analysis session.</param>
    /// <returns>The extracted knowledge-graph payload for the supplied solution.</returns>
    Task<KnowledgeGraphExtractionPayload> Extract(
        string targetPath,
        Solution solution,
        IRepositoryWorkspace repositoryWorkspace,
        IProgress<IndexingProgress>? progress = null,
        IReadOnlyCollection<string>? diagnostics = null,
        CancellationToken ct = default);

    Task<KnowledgeGraphExtractionPayload> Extract(
        string targetPath,
        Project project,
        IRepositoryWorkspace repositoryWorkspace)
        => Extract(
            targetPath,
            project,
            repositoryWorkspace,
            progress: null,
            diagnostics: null,
            CancellationToken.None);

    Task<KnowledgeGraphExtractionPayload> Extract(
        string targetPath,
        Project project,
        IRepositoryWorkspace repositoryWorkspace,
        CancellationToken ct)
        => Extract(
            targetPath,
            project,
            repositoryWorkspace,
            progress: null,
            diagnostics: null,
            ct);

    /// <summary>
    /// Extracts a full knowledge-graph payload from the supplied Roslyn project.
    /// </summary>
    /// <param name="targetPath">The canonical target path being analyzed.</param>
    /// <param name="project">The loaded Roslyn project to analyze.</param>
    /// <param name="repositoryWorkspace">The repository workspace used for path normalization.</param>
    /// <param name="progress">Optional progress reporter for the current analysis session.</param>
    /// <param name="diagnostics">Optional diagnostics collected during workspace loading.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current analysis session.</param>
    /// <returns>The extracted knowledge-graph payload for the supplied project.</returns>
    Task<KnowledgeGraphExtractionPayload> Extract(
        string targetPath,
        Project project,
        IRepositoryWorkspace repositoryWorkspace,
        IProgress<IndexingProgress>? progress = null,
        IReadOnlyCollection<string>? diagnostics = null,
        CancellationToken ct = default);

    Task<KnowledgeGraphExtractionPayload> ExtractIncremental(
        string targetPath,
        Solution solution,
        IRepositoryWorkspace repositoryWorkspace,
        IReadOnlyList<WorkspaceFileChange> changedFiles)
        => ExtractIncremental(
            targetPath,
            solution,
            repositoryWorkspace,
            changedFiles,
            progress: null,
            diagnostics: null,
            CancellationToken.None);

    Task<KnowledgeGraphExtractionPayload> ExtractIncremental(
        string targetPath,
        Solution solution,
        IRepositoryWorkspace repositoryWorkspace,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        CancellationToken ct)
        => ExtractIncremental(
            targetPath,
            solution,
            repositoryWorkspace,
            changedFiles,
            progress: null,
            diagnostics: null,
            ct);

    /// <summary>
    /// Extracts an incremental knowledge-graph payload for the supplied Roslyn solution after a set of workspace file
    /// changes has already been applied in memory.
    /// </summary>
    /// <param name="targetPath">The canonical target path being analyzed.</param>
    /// <param name="solution">The updated Roslyn solution to analyze incrementally.</param>
    /// <param name="repositoryWorkspace">The repository workspace used for path normalization.</param>
    /// <param name="changedFiles">The file changes that triggered incremental analysis.</param>
    /// <param name="progress">Optional progress reporter for the current analysis session.</param>
    /// <param name="diagnostics">Optional diagnostics collected during workspace loading or document refresh.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current analysis session.</param>
    /// <returns>The incremental knowledge-graph payload for the supplied changed files.</returns>
    Task<KnowledgeGraphExtractionPayload> ExtractIncremental(
        string targetPath,
        Solution solution,
        IRepositoryWorkspace repositoryWorkspace,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        IProgress<IndexingProgress>? progress = null,
        IReadOnlyCollection<string>? diagnostics = null,
        CancellationToken ct = default);
}
