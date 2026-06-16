using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.HybridSearch.Abstractions;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Shared;

namespace SharpSense.Infrastructure.HybridSearch;

/// <summary>
/// Runs the persisted hybrid-search pipeline against <c>CodeNodes</c>. A single SQLite CTE ranks candidates using
/// Reciprocal Rank Fusion of BM25 keyword scores (FTS5 over <c>CodeNodeSearch</c>) and vector cosine distance
/// (sqlite-vec over <c>CodeNodes.VectorEmbedding</c>). When <c>IncludeMemories</c> is set, the keyword match
/// query and query vector are also evaluated against the <c>MemoryNodes</c> table — joined to the owning
/// <c>CodeNodes</c> row — so memory-tagged content can promote its target node. Project/NodeType filters and
/// the optional tag filter push into the CTEs so ranking only happens over the relevant subset.
/// </summary>
public sealed class HybridSearcher(
    IDbContextFactory<SharpSenseDbContext> dbContextFactory,
    IEmbeddingGenerator embeddingsService,
    IKeywordCandidateProvider keywordProvider)
    : IHybridSearcher
{
    private const int CandidateLimitMultiplier = 20;
    private const int MinimumCandidateLimit = 50;
    private const int MaximumCandidateLimit = 250;
    private const int RrfConstant = 60;

    public async Task<HybridSearchResult> Search(HybridSearchQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SearchText);

        if (query.Limit <= 0)
        {
            return new HybridSearchResult(query.SearchText, []);
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var candidateLimit = GetCandidateLimit(query.Limit);

        int? projectNodeId = null;
        if (!string.IsNullOrWhiteSpace(query.ProjectId))
        {
            projectNodeId = await context.GraphNodes
                .AsNoTracking()
                .Where(graphNode => graphNode.Kind == GraphNodeKind.Project && graphNode.CanonicalId == query.ProjectId)
                .Select(static graphNode => (int?)graphNode.Id)
                .FirstOrDefaultAsync(ct);

            if (projectNodeId is null)
            {
                return new HybridSearchResult(query.SearchText, []);
            }
        }

        var matchQuery = await keywordProvider.GetMatchQueryAsync(query, ct);
        if (string.IsNullOrWhiteSpace(matchQuery))
        {
            return new HybridSearchResult(query.SearchText, []);
        }

        var embedding = await embeddingsService.Generate(query.SearchText, ct).ConfigureAwait(false);
        var queryVector = embedding.Vector;

        var rankedIds = await ExecuteHybridRankingAsync(
            context,
            matchQuery,
            queryVector,
            projectNodeId,
            query.IncludedNodeTypes ?? [],
            query.TagFilters,
            query.IncludeMemories,
            candidateLimit,
            query.Limit,
            ct);

        if (rankedIds.Length == 0)
        {
            return new HybridSearchResult(query.SearchText, []);
        }

        var codeNodesQuery = context.CodeNodes.AsNoTracking();
        var projectedQuery = CodeNodeNavigationQueries.ProjectCodeNodes(context, codeNodesQuery);
        var projectedById = await projectedQuery
            .Where(codeNode => rankedIds.Contains(codeNode.Id))
            .ToDictionaryAsync(static codeNode => codeNode.Id, ct);

        var orderedNodes = rankedIds
            .Where(projectedById.ContainsKey)
            .Select(id => projectedById[id])
            .ToArray();

        return new HybridSearchResult(
            query.SearchText,
            HybridSearchMapper.ToSearchHit(orderedNodes));
    }

    private static async Task<int[]> ExecuteHybridRankingAsync(
        SharpSenseDbContext context,
        string matchQuery,
        float[] queryVector,
        int? projectNodeId,
        IReadOnlyCollection<NodeType> includedNodeTypes,
        string[]? tagFilters,
        bool includeMemories,
        int candidateLimit,
        int resultLimit,
        CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = BuildRrfQuery(tagFilters, includeMemories);

        AddParameter(command, "@matchQuery", matchQuery);
        AddParameter(command, "@queryVector", ConvertToBytes(queryVector));
        AddParameter(command, "@projectNodeId", (object?)projectNodeId ?? DBNull.Value);
        AddParameter(command, "@nodeTypes", SerializeNodeTypes(includedNodeTypes));
        AddParameter(command, "@nodeTypeCount", includedNodeTypes.Count);
        AddParameter(command, "@candidateLimit", candidateLimit);
        AddParameter(command, "@resultLimit", resultLimit);
        AddParameter(command, "@rrf", RrfConstant);
        AddParameter(command, "@memorySearchText", matchQuery.ToLowerInvariant());

        MemorySearchSql.AddTagFilterParameters(command, tagFilters);

        var rankedIds = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0))
            {
                rankedIds.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture));
            }
        }

        return rankedIds.ToArray();
    }

    private static string BuildRrfQuery(string[]? tagFilters, bool includeMemories)
    {
        var memoryTagFilterClause = includeMemories
            ? MemorySearchSql.BuildTagFilterClause(tagFilters, "m.TagsJson")
            : string.Empty;

        var memoryCtes = includeMemories
            ? $$"""

            ,
            memory_bm25 AS (
                SELECT c.Id AS Id, ROW_NUMBER() OVER (ORDER BY memory_hits.score DESC) AS rank
                FROM (
                    SELECT m.TargetFullyQualifiedName AS fqn,
                           (CASE WHEN instr(lower(m.Content), @memorySearchText) > 0 THEN 12 ELSE 0 END
                            + CASE WHEN instr(lower(m.TargetFullyQualifiedName), @memorySearchText) > 0 THEN 8 ELSE 0 END
                            + CASE WHEN EXISTS (SELECT 1 FROM json_each(m.TagsJson) AS tag WHERE instr(lower(CAST(tag.value AS TEXT)), @memorySearchText) > 0) THEN 10 ELSE 0 END
                           ) AS score
                    FROM MemoryNodes m
                    WHERE (instr(lower(m.Content), @memorySearchText) > 0
                           OR instr(lower(m.TargetFullyQualifiedName), @memorySearchText) > 0
                           OR EXISTS (SELECT 1 FROM json_each(m.TagsJson) AS tag WHERE instr(lower(CAST(tag.value AS TEXT)), @memorySearchText) > 0)){{memoryTagFilterClause}}
                ) memory_hits
                INNER JOIN CodeNodes c ON c.FullyQualifiedName = memory_hits.fqn
                WHERE (@projectNodeId IS NULL OR c.ProjectNodeId = @projectNodeId)
                  AND (@nodeTypeCount = 0 OR c.NodeType IN (SELECT value FROM json_each(@nodeTypes)))
                ORDER BY memory_hits.score DESC
                LIMIT @candidateLimit
            ),
            memory_vec AS (
                SELECT c.Id AS Id, ROW_NUMBER() OVER (ORDER BY vec_distance_cosine(m.VectorEmbedding, vec_f32(@queryVector)) ASC) AS rank
                FROM MemoryNodes m
                INNER JOIN CodeNodes c ON c.FullyQualifiedName = m.TargetFullyQualifiedName
                WHERE m.VectorEmbedding IS NOT NULL{{memoryTagFilterClause}}
                  AND (@projectNodeId IS NULL OR c.ProjectNodeId = @projectNodeId)
                  AND (@nodeTypeCount = 0 OR c.NodeType IN (SELECT value FROM json_each(@nodeTypes)))
                ORDER BY vec_distance_cosine(m.VectorEmbedding, vec_f32(@queryVector)) ASC
                LIMIT @candidateLimit
            )
            """
            : string.Empty;

        var rankedSourcesUnion = includeMemories
            ? """
                SELECT 'code_bm25' AS src, Id, rank FROM code_bm25
                UNION ALL
                SELECT 'code_vec', Id, rank FROM code_vec
                UNION ALL
                SELECT 'memory_bm25', Id, rank FROM memory_bm25
                UNION ALL
                SELECT 'memory_vec', Id, rank FROM memory_vec
              """
            : """
                SELECT 'code_bm25' AS src, Id, rank FROM code_bm25
                UNION ALL
                SELECT 'code_vec', Id, rank FROM code_vec
              """;

        return
            $$"""
            WITH code_bm25 AS (
                SELECT CAST(c.Id AS INTEGER) AS Id, ROW_NUMBER() OVER (ORDER BY bm25(CodeNodeSearch) ASC) AS rank
                FROM CodeNodeSearch
                INNER JOIN CodeNodes c ON c.Id = CodeNodeSearch.Id
                WHERE CodeNodeSearch MATCH @matchQuery
                  AND (@projectNodeId IS NULL OR c.ProjectNodeId = @projectNodeId)
                  AND (@nodeTypeCount = 0 OR c.NodeType IN (SELECT value FROM json_each(@nodeTypes)))
                ORDER BY bm25(CodeNodeSearch) ASC
                LIMIT @candidateLimit
            ),
            code_vec AS (
                SELECT c.Id AS Id, ROW_NUMBER() OVER (ORDER BY vec_distance_cosine(c.VectorEmbedding, vec_f32(@queryVector)) ASC) AS rank
                FROM CodeNodes c
                WHERE c.VectorEmbedding IS NOT NULL
                  AND (@projectNodeId IS NULL OR c.ProjectNodeId = @projectNodeId)
                  AND (@nodeTypeCount = 0 OR c.NodeType IN (SELECT value FROM json_each(@nodeTypes)))
                ORDER BY vec_distance_cosine(c.VectorEmbedding, vec_f32(@queryVector)) ASC
                LIMIT @candidateLimit
            ){{memoryCtes}},
            ranked_sources AS (
            {{rankedSourcesUnion}}
            )
            SELECT Id
            FROM ranked_sources
            GROUP BY Id
            ORDER BY SUM(1.0 / (@rrf + rank)) DESC
            LIMIT @resultLimit;
            """;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string SerializeNodeTypes(IReadOnlyCollection<NodeType> nodeTypes)
        => nodeTypes.Count == 0
            ? "[]"
            : "[" + string.Join(",", nodeTypes.Select(static nodeType => $"\"{nodeType}\"")) + "]";

    private static byte[] ConvertToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetCandidateLimit(int limit)
        => Math.Clamp(limit * CandidateLimitMultiplier, MinimumCandidateLimit, MaximumCandidateLimit);
}
