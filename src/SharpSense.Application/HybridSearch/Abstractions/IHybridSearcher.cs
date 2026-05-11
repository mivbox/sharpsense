using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.HybridSearch.HybridSearch.Models;

namespace SharpSense.Application.HybridSearch.Abstractions;

/// <summary>
/// Executes the persisted hybrid-search pipeline for code nodes, blending keyword matching, repository filters, and
/// vector similarity into the ranked hit set consumed by the CLI and MCP read surfaces.
/// </summary>
public interface IHybridSearcher
{
    /// <summary>
    /// Searches the indexed repository using the supplied query and returns the ranked code-node hits.
    /// </summary>
    Task<HybridSearchResult> Search(HybridSearchQuery query,
        CancellationToken ct);
}
