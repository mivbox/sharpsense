using SharpSense.Application.HybridSearch.HybridSearch.Models;

namespace SharpSense.Infrastructure.HybridSearch;

/// <summary>
/// Translates free-text search input into the SQLite FTS5 MATCH expression that the hybrid-search pipeline forwards
/// to its combined keyword + vector ranking query.
/// </summary>
internal interface IKeywordCandidateProvider
{
    /// <summary>
    /// Returns a safely quoted prefix expression for plain-text input. FTS operators and column filters
    /// have no special meaning. Returns an empty string when no searchable terms remain.
    /// </summary>
    Task<string> GetMatchQuery(HybridSearchQuery query, CancellationToken ct);
}

