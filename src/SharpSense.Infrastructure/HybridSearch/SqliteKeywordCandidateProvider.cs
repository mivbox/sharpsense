using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.HybridSearch;

public sealed class SqliteKeywordCandidateProvider : IKeywordCandidateProvider
{
    public async Task<int[]> GetCandidateIdsAsync(SharpSenseDbContext context,
        string searchText,
        int limit,
        CancellationToken ct)
    {
        var tokens = HybridSearchTokenizer.Tokenize(searchText);
        if (tokens.Length == 0)
        {
            return [];
        }

        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(ct);
        }

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

        var candidateIds = new List<int>(limit);
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

    private static string BuildMatchQuery(IEnumerable<string> tokens)
        => string.Join(
            " OR ",
            tokens.Select(static token => $"\"{EscapeMatchToken(token)}\""));

    private static string EscapeMatchToken(string token)
        => token.Replace("\"", "\"\"", StringComparison.Ordinal);
}

