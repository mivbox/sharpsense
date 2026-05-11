using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.HybridSearch;

/// <summary>
/// Selects the keyword-ranked candidate node ids that the hybrid-search pipeline should hydrate before it evaluates
/// vector similarity. This boundary keeps SQLite FTS query construction out of <see cref="HybridSearcher"/> while
/// letting the orchestrator reuse the same repository DbContext for the full search request.
/// </summary>
public interface IKeywordCandidateProvider
{
    /// <summary>
    /// Returns the best keyword-matching code-node ids for the supplied search text, ordered for candidate hydration.
    /// </summary>
    Task<int[]> GetCandidateIdsAsync(SharpSenseDbContext context,
        string searchText,
        int limit,
        CancellationToken ct);
}

