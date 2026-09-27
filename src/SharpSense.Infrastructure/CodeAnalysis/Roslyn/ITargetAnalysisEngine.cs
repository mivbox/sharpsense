using Microsoft.CodeAnalysis;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

/// <summary>
/// Transforms already-loaded Roslyn targets into project nodes, code nodes, and dependency edges without owning the
/// physical workspace-loading concern. Implementations operate on an in-memory <see cref="Solution"/>
/// so analysis can be tested independently from MSBuild and disk-backed workspaces.
/// </summary>
internal interface ITargetAnalysisEngine
{
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
}
