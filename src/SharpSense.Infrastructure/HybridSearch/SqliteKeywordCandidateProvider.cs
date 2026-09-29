using SharpSense.Application.HybridSearch.HybridSearch.Models;

namespace SharpSense.Infrastructure.HybridSearch;

/// <summary>Builds a safely quoted FTS5 prefix expression from plain-text search terms.</summary>
internal sealed class SqliteKeywordCandidateProvider : IKeywordCandidateProvider
{
    private const int MinimumTokenLength = 2;

    /// <inheritdoc />
    public Task<string> GetMatchQuery(HybridSearchQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var matchQuery = BuildMatchQuery(query.SearchText);

        return Task.FromResult(matchQuery);
    }

    private static string BuildMatchQuery(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return string.Empty;
        }

        var prefixTerms = HybridSearchTokenizer
            .Tokenize(searchText)
            .Where(static token => token.Length >= MinimumTokenLength && token.Any(char.IsLetterOrDigit))
            .Select(static token => $"\"{token.Replace("\"", "\"\"")}\"*")
            .ToArray();

        return prefixTerms.Length == 0
            ? string.Empty
            : string.Join(" OR ", prefixTerms);
    }
}
