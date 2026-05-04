using SharpSense.Application.Indexing.Models;

namespace SharpSense.Application.Indexing.Abstractions;

/// <summary>
/// Persists the extracted knowledge graph for the indexing slice without exposing persistence technology details to the
/// Application layer. Implementations own transactions, delete/replace semantics, integer id preservation, and FTS
/// synchronization for both full and incremental writes.
/// </summary>
public interface IKnowledgeGraphRepository
{
    /// <summary>
    /// Replaces the persisted graph for the current target with the supplied extracted nodes.
    /// </summary>
    /// <param name="extractedNodes">The fully extracted target graph ready for persistence.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current indexing operation.</param>
    Task ReplaceTarget(
        ExtractedNodes extractedNodes,
        CancellationToken ct);

    /// <summary>
    /// Replaces the persisted graph data owned by the supplied repository-relative file paths with the supplied extracted
    /// delta.
    /// </summary>
    /// <param name="relativeFilePaths">Repository-relative files whose graph data should be replaced.</param>
    /// <param name="extractedNodes">The replacement graph data for the affected files.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current incremental operation.</param>
    Task ReplaceWorkspaceFiles(
        IReadOnlyList<string> relativeFilePaths,
        ExtractedNodes extractedNodes,
        CancellationToken ct);

    /// <summary>
    /// Returns the normalized repository-relative persisted document paths beneath the supplied repository-relative
    /// directory path. This allows incremental directory events to be expanded into file-level replacements without
    /// coupling the Application layer to persistence tables or SQL shape.
    /// </summary>
    /// <param name="relativeDirectoryPath">The repository-relative directory whose persisted document paths should be returned.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current read operation.</param>
    /// <returns>The persisted repository-relative document paths beneath the directory.</returns>
    Task<IReadOnlyList<string>> GetPersistedDocumentPathsUnderDirectory(
        string relativeDirectoryPath,
        CancellationToken ct);
}
