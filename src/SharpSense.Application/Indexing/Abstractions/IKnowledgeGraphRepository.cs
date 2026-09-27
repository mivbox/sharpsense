using SharpSense.Application.Indexing.Models;

namespace SharpSense.Application.Indexing.Abstractions;

/// <summary>
/// Persists the extracted knowledge graph for the indexing slice without exposing persistence technology details to the
/// Application layer. Implementations own transactions, delete/replace semantics, integer id preservation, and FTS
/// synchronization for complete workspace commits.
/// </summary>
public interface IKnowledgeGraphRepository
{
    /// <summary>
    /// Replaces the persisted graph for the selected workspace with the supplied extracted nodes.
    /// </summary>
    /// <param name="extractedNodes">The fully extracted workspace graph ready for persistence.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current indexing operation.</param>
    Task ReplaceWorkspace(
        ExtractedNodes extractedNodes,
        CancellationToken ct);

    /// <summary>
    /// Returns the persisted code nodes currently stored for the active repository. This lets the indexing slice reuse
    /// existing vectors and detect no-op full analysis runs without exposing table details.
    /// </summary>
    /// <param name="ct"><see cref="CancellationToken"/> for the current read operation.</param>
    /// <returns>The persisted code nodes for the current repository.</returns>
    Task<IReadOnlyList<IndexedCodeNode>> GetPersistedCodeNodes(CancellationToken ct);
}
