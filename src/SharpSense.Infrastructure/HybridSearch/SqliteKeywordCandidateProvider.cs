using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.HybridSearch;

public sealed class SqliteKeywordCandidateProvider : IKeywordCandidateProvider
{
    public async Task<int[]> GetCandidateIdsAsync(SharpSenseDbContext context,
        HybridSearchQuery query,
        int limit,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tokens = HybridSearchTokenizer.Tokenize(query.SearchText);
        if (tokens.Length == 0)
        {
            return [];
        }

        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(ct);
        }

        var codeCandidateIds = await GetCodeCandidateIds(connection, tokens, limit, ct);
        if (!query.IncludeMemories)
        {
            return codeCandidateIds;
        }

        var memoryCandidateIds = await GetMemoryCandidateIds(connection, query, tokens, limit, ct);
        return [..
            codeCandidateIds
                .Concat(memoryCandidateIds)
                .Distinct()
                .Take(limit)];
    }

    private static string BuildMatchQuery(IEnumerable<string> tokens)
        => string.Join(
            " OR ",
            tokens.Select(static token => $"\"{EscapeMatchToken(token)}\""));

    private static async Task<int[]> GetCodeCandidateIds(
        DbConnection connection,
        IReadOnlyCollection<string> tokens,
        int limit,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT CAST(Id AS INTEGER)
            FROM CodeNodeSearch
            WHERE CodeNodeSearch MATCH $matchQuery
            ORDER BY bm25(CodeNodeSearch)
            LIMIT {limit};
            """;

        var matchQueryParameter = command.CreateParameter();
        matchQueryParameter.ParameterName = "$matchQuery";
        matchQueryParameter.Value = BuildMatchQuery(tokens);
        command.Parameters.Add(matchQueryParameter);

        return await ReadCandidateIds(command, ct);
    }

    private static async Task<int[]> GetMemoryCandidateIds(
        DbConnection connection,
        HybridSearchQuery query,
        IReadOnlyList<string> tokens,
        int limit,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        var scoreExpression = BuildMemoryKeywordScoreExpression(command, query.SearchText, tokens);
        var tagFilterClause = MemorySearchSql.BuildTagFilterClause(command, query.TagFilters, "MemoryNodes.TagsJson");

        command.CommandText = $$"""
            SELECT CodeNodes.Id,
                   MAX({{scoreExpression}}) AS Score,
                   MAX(MemoryNodes.CreatedAt) AS LatestCreatedAt
            FROM MemoryNodes
            INNER JOIN CodeNodes ON CodeNodes.FullyQualifiedName = MemoryNodes.TargetFullyQualifiedName
            WHERE {{scoreExpression}} > 0{{tagFilterClause}}
            GROUP BY CodeNodes.Id
            ORDER BY Score DESC, LatestCreatedAt DESC, CodeNodes.Id
            LIMIT {{limit}};
            """;

        return await ReadCandidateIds(command, ct);
    }

    private static string BuildMemoryKeywordScoreExpression(
        DbCommand command,
        string searchText,
        IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);

        var searchTextParameter = command.CreateParameter();
        searchTextParameter.ParameterName = "$searchText";
        searchTextParameter.Value = searchText.ToLowerInvariant();
        command.Parameters.Add(searchTextParameter);

        var scoreTerms = new List<string>
        {
            "CASE WHEN instr(lower(MemoryNodes.Content), $searchText) > 0 THEN 12 ELSE 0 END",
            "CASE WHEN instr(lower(MemoryNodes.TargetFullyQualifiedName), $searchText) > 0 THEN 8 ELSE 0 END",
            "CASE WHEN EXISTS (SELECT 1 FROM json_each(MemoryNodes.TagsJson) AS tag WHERE instr(lower(CAST(tag.value AS TEXT)), $searchText) > 0) THEN 10 ELSE 0 END"
        };

        for (var index = 0; index < tokens.Count; index++)
        {
            var tokenParameter = command.CreateParameter();
            tokenParameter.ParameterName = $"$token{index}";
            tokenParameter.Value = tokens[index].ToLowerInvariant();
            command.Parameters.Add(tokenParameter);

            scoreTerms.Add($"CASE WHEN instr(lower(MemoryNodes.Content), $token{index}) > 0 THEN 3 ELSE 0 END");
            scoreTerms.Add($"CASE WHEN EXISTS (SELECT 1 FROM json_each(MemoryNodes.TagsJson) AS tag WHERE instr(lower(CAST(tag.value AS TEXT)), $token{index}) > 0) THEN 4 ELSE 0 END");
        }

        return $"({string.Join(" + ", scoreTerms)})";
    }

    private static async Task<int[]> ReadCandidateIds(DbCommand command, CancellationToken ct)
    {
        var candidateIds = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0))
            {
                candidateIds.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture));
            }
        }

        return candidateIds.ToArray();
    }

    private static string EscapeMatchToken(string token)
        => token.Replace("\"", "\"\"", StringComparison.Ordinal);
}
