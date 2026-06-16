using SharpSense.Application.HybridSearch.HybridSearch.Models;

namespace SharpSense.Infrastructure.HybridSearch;

/// <summary>
/// Translates free-text search input into the SQLite FTS5 MATCH expression that the hybrid-search pipeline forwards
/// to its combined keyword + vector ranking query.
/// </summary>
public interface IKeywordCandidateProvider
{
    /// <summary>
    /// Returns the FTS5 MATCH expression for the supplied query. Tokens are expanded to prefix terms
    /// (<c>token*</c>) so partial words match. When the input already contains standard FTS5 operators
    /// (<c>OR</c>, <c>NOT</c>, <c>column:</c>, <c>NEAR</c>, quoted phrases), it is forwarded verbatim.
    /// </summary>
    Task<string> GetMatchQueryAsync(HybridSearchQuery query, CancellationToken ct);
}

