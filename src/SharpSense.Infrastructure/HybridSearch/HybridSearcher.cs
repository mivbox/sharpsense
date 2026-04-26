using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Features.HybridSearch.Contracts;
using SharpSense.Application.Features.HybridSearch.HybridSearch;
using SharpSense.Application.Features.HybridSearch.Infrastructure;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.HybridSearch;

public sealed class HybridSearcher(
    IDbContextFactory<SharpSenseDbContext> dbContextFactory,
    IEmbeddingGenerator embeddingsService)
    : IHybridSearcher
{
    private const int _candidateLimitMultiplier = 20;
    private const int _minimumCandidateLimit = 50;
    private const int _maximumCandidateLimit = 250;

    private static readonly char[] _searchTokenSeparators =
    [
        ' ',
        '\t',
        '\r',
        '\n',
        '.',
        ':',
        '-',
        '_',
        '/',
        '\\',
        '(',
        ')',
        '[',
        ']',
        '<',
        '>',
        ',',
        ';'
    ];

    public async Task<HybridSearchResult> Search(HybridSearchQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SearchText);

        if (query.Limit <= 0)
        {
            return new HybridSearchResult(query.SearchText, []);
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var codeNodesQuery = context.CodeNodes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.ProjectId))
        {
            codeNodesQuery = codeNodesQuery.Where(codeNode => codeNode.ProjectId == query.ProjectId);
        }

        if (query.IncludedNodeTypes is { Length: > 0 })
        {
            codeNodesQuery = codeNodesQuery.Where(codeNode => query.IncludedNodeTypes.Contains(codeNode.NodeType));
        }

        var candidateLimit = GetCandidateLimit(query.Limit);
        var candidateIds = await LoadKeywordCandidateIds(
                context,
                query.SearchText,
                candidateLimit,
                ct)
            ;

        var codeNodes = candidateIds.Length > 0
            ? await codeNodesQuery
                .Where(codeNode => candidateIds.Contains(codeNode.Id))
                .ToArrayAsync(ct)
                .ConfigureAwait(false)
            : await codeNodesQuery
                .Where(codeNode =>
                    codeNode.DisplayName.Contains(query.SearchText) ||
                    codeNode.FullyQualifiedName.Contains(query.SearchText) ||
                    codeNode.Summary.Contains(query.SearchText) ||
                    codeNode.RelativeFilePath.Contains(query.SearchText))
                .OrderBy(codeNode => codeNode.FullyQualifiedName)
                .ThenBy(codeNode => codeNode.Id)
                .Take(candidateLimit)
                .ToArrayAsync(ct)
                ;
        if (codeNodes.Length == 0)
        {
            return new HybridSearchResult(query.SearchText, []);
        }

        var queryEmbedding = codeNodes.Any(static codeNode => codeNode.VectorEmbedding is { Length: > 0 })
            ? (await embeddingsService.Generate(query.SearchText, ct).ConfigureAwait(false)).Vector
            : null;
        var tokens = Tokenize(query.SearchText);
        var vectorScores = queryEmbedding is { Length: > 0 }
            ? await LoadVectorScores(
                    context,
                    codeNodes.Select(static codeNode => codeNode.Id).ToArray(),
                    queryEmbedding,
                    ct)
                .ConfigureAwait(false)
            : new Dictionary<int, float>();

        var rankedNodes = codeNodes
            .Select(
                codeNode =>
                {
                    var keywordScore = ComputeKeywordScore(codeNode, query.SearchText, tokens);
                    var vectorScore = vectorScores.GetValueOrDefault(codeNode.Id, 0f);
                    return new RankedSearchResult(codeNode, keywordScore, vectorScore, keywordScore + (vectorScore * 40f));
                })
            .Where(static result => result.KeywordScore > 0f || result.VectorScore > 0f)
            .OrderByDescending(static result => result.TotalScore)
            .ThenByDescending(static result => result.KeywordScore)
            .ThenByDescending(static result => result.VectorScore)
            .ThenBy(static result => result.Node.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static result => result.Node.Id)
            .Take(query.Limit)
            .Select(static result => result.Node)
            .ToArray();

        return new HybridSearchResult(query.SearchText, HybridSearchMapper.ToSearchHit(rankedNodes));
    }

    private async Task<int[]> LoadKeywordCandidateIds(
        SharpSenseDbContext dbContext,
        string searchText,
        int candidateLimit,
        CancellationToken ct)
    {
        var tokens = Tokenize(searchText);
        if (tokens.Length == 0)
        {
            return [];
        }

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT CAST(Id AS INTEGER)
            FROM CodeNodeSearch
            WHERE CodeNodeSearch MATCH $matchQuery
            ORDER BY bm25(CodeNodeSearch)
            LIMIT {candidateLimit};
            """;

        var matchQueryParameter = command.CreateParameter();
        matchQueryParameter.ParameterName = "$matchQuery";
        matchQueryParameter.Value = BuildMatchQuery(tokens);
        command.Parameters.Add(matchQueryParameter);

        var candidateIds = new List<int>(candidateLimit);
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

    private async Task<Dictionary<int, float>> LoadVectorScores(
        SharpSenseDbContext dbContext,
        IReadOnlyList<int> candidateIds,
        float[] queryVector,
        CancellationToken ct)
    {
        if (candidateIds.Count == 0)
        {
            return new Dictionary<int, float>();
        }

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync(ct);
        }

        await using var command = connection.CreateCommand();
        var parameterNames = new string[candidateIds.Count];
        for (var index = 0; index < candidateIds.Count; index++)
        {
            var parameterName = $"$id{index}";
            parameterNames[index] = parameterName;

            var idParameter = command.CreateParameter();
            idParameter.ParameterName = parameterName;
            idParameter.Value = candidateIds[index];
            command.Parameters.Add(idParameter);
        }

        var queryVectorParameter = command.CreateParameter();
        queryVectorParameter.ParameterName = "$queryVector";
        queryVectorParameter.Value = ConvertToBytes(queryVector);
        command.Parameters.Add(queryVectorParameter);

        command.CommandText = $$"""
            SELECT Id, vec_distance_cosine(VectorEmbedding, vec_f32($queryVector))
            FROM CodeNodes
            WHERE VectorEmbedding IS NOT NULL
              AND Id IN ({{string.Join(", ", parameterNames)}});
            """;

        var vectorScores = new Dictionary<int, float>(candidateIds.Count);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            var codeNodeId = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            var distance = Convert.ToSingle(reader.GetValue(1), CultureInfo.InvariantCulture);
            vectorScores[codeNodeId] = Math.Clamp(1f - distance, 0f, 1f);
        }

        return vectorScores;
    }

    private static string[] Tokenize(string searchText)
        => searchText
            .Split(_searchTokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static int GetCandidateLimit(int limit)
        => Math.Clamp(limit * _candidateLimitMultiplier, _minimumCandidateLimit, _maximumCandidateLimit);

    private static string BuildMatchQuery(IEnumerable<string> tokens)
        => string.Join(
            " OR ",
            tokens.Select(static token => $"\"{EscapeMatchToken(token)}\""));

    private static string EscapeMatchToken(string token)
        => token.Replace("\"", "\"\"", StringComparison.Ordinal);

    private static float ComputeKeywordScore(CodeNode codeNode, string searchText, IReadOnlyList<string> tokens)
    {
        var score = 0f;

        if (string.Equals(codeNode.FullyQualifiedName, searchText, StringComparison.OrdinalIgnoreCase))
        {
            score += 120f;
        }

        if (Contains(codeNode.DisplayName, searchText))
        {
            score += 90f;
        }

        if (Contains(codeNode.FullyQualifiedName, searchText))
        {
            score += 70f;
        }

        if (Contains(codeNode.Summary, searchText))
        {
            score += 30f;
        }

        if (Contains(codeNode.RelativeFilePath, searchText))
        {
            score += 20f;
        }

        foreach (var token in tokens)
        {
            if (Contains(codeNode.DisplayName, token))
            {
                score += 16f;
            }

            if (Contains(codeNode.FullyQualifiedName, token))
            {
                score += 12f;
            }

            if (Contains(codeNode.Summary, token))
            {
                score += 6f;
            }

            if (Contains(codeNode.RelativeFilePath, token))
            {
                score += 4f;
            }
        }

        return score;
    }

    private static bool Contains(string value, string searchText)
        => value.Contains(searchText, StringComparison.OrdinalIgnoreCase);

    private static byte[] ConvertToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private sealed record RankedSearchResult(CodeNode Node, float KeywordScore, float VectorScore, float TotalScore);
}
