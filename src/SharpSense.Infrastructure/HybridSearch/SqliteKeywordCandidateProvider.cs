using SharpSense.Application.HybridSearch.HybridSearch.Models;

using System.Text.RegularExpressions;

namespace SharpSense.Infrastructure.HybridSearch;

/// <summary>
/// Builds the SQLite FTS5 MATCH expression for <see cref="IKeywordCandidateProvider"/>. Bare tokens are expanded to
/// prefix terms (<c>token*</c>) so partial words match. Standard FTS5 operators (<c>OR</c>, <c>NOT</c>,
/// <c>column:</c>, <c>NEAR</c>, quoted phrases, trailing <c>*</c>) are detected and forwarded verbatim so power
/// users can scope tightly without the orchestrator having to parse the query.
/// </summary>
internal sealed partial class SqliteKeywordCandidateProvider : IKeywordCandidateProvider
{
    private const int MinimumTokenLength = 2;
    private const string ColumnNamePattern = @"(?:DisplayName|FullyQualifiedName|SearchText|RelativeFilePath)";

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

        if (ContainsFtsOperators(searchText))
        {
            return searchText.Trim();
        }

        var prefixTerms = HybridSearchTokenizer
            .Tokenize(searchText)
            .Where(static token => token.Length >= MinimumTokenLength)
            .Select(static token => $"\"{token.Replace("\"", "\"\"")}\"*")
            .ToArray();

        return prefixTerms.Length == 0
            ? string.Empty
            : string.Join(" OR ", prefixTerms);
    }

    private static bool ContainsFtsOperators(string searchText)
        => searchText.Contains(" OR ", StringComparison.Ordinal)
           || searchText.Contains(" NOT ", StringComparison.Ordinal)
           || searchText.Contains(" AND ", StringComparison.Ordinal)
           || NearOperatorPattern().IsMatch(searchText)
           || searchText.Contains('"')
           || ColumnFilterPattern().IsMatch(searchText)
           || searchText.Contains('*');

    [GeneratedRegex(@"\bNEAR\s*\(")]
    private static partial Regex NearOperatorPattern();

    [GeneratedRegex(@"(?:^|\s|\()-?\s*(?:" + ColumnNamePattern + @"|\{\s*" + ColumnNamePattern +
        @"(?:\s+" + ColumnNamePattern + @")*\s*\})\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ColumnFilterPattern();
}
