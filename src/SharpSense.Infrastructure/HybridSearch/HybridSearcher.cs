using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.HybridSearch.Abstractions;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Shared;

namespace SharpSense.Infrastructure.HybridSearch;

public sealed class HybridSearcher(
    IDbContextFactory<SharpSenseDbContext> dbContextFactory,
    IEmbeddingGenerator embeddingsService,
    IMemoryRepository memoryRepository,
    IKeywordCandidateProvider keywordProvider,
    IVectorScorer vectorScorer)
    : IHybridSearcher
{
    private const int CandidateLimitMultiplier = 20;
    private const int MinimumCandidateLimit = 50;
    private const int MaximumCandidateLimit = 250;

    public async Task<HybridSearchResult> Search(HybridSearchQuery query,
        CancellationToken ct)
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
            var projectNodeId = await context.GraphNodes
                .AsNoTracking()
                .Where(graphNode =>
                    graphNode.Kind == GraphNodeKind.Project &&
                    graphNode.CanonicalId == query.ProjectId)
                .Select(static graphNode => (int?)graphNode.Id)
                .FirstOrDefaultAsync(ct);
            if (projectNodeId is null)
            {
                return new HybridSearchResult(query.SearchText, []);
            }

            codeNodesQuery = codeNodesQuery.Where(codeNode => codeNode.ProjectNodeId == projectNodeId);
        }

        if (query.IncludedNodeTypes is { Length: > 0 })
        {
            codeNodesQuery = codeNodesQuery.Where(codeNode => query.IncludedNodeTypes.Contains(codeNode.NodeType));
        }

        var projectedCodeNodesQuery = CodeNodeNavigationQueries.ProjectCodeNodes(context, codeNodesQuery);

        var candidateLimit = GetCandidateLimit(query.Limit);
        var candidateIds = await keywordProvider.GetCandidateIdsAsync(context,
            query,
            candidateLimit,
            ct);

        var codeNodes = candidateIds.Length > 0
            ? await projectedCodeNodesQuery
                .Where(codeNode => candidateIds.Contains(codeNode.Id))
                .ToArrayAsync(ct)
                .ConfigureAwait(false)
            : await projectedCodeNodesQuery
                .Where(codeNode =>
                    codeNode.DisplayName.Contains(query.SearchText) ||
                    codeNode.FullyQualifiedName.Contains(query.SearchText) ||
                    codeNode.SearchText.Contains(query.SearchText) ||
                    codeNode.RelativeFilePath.Contains(query.SearchText))
                .OrderBy(codeNode => codeNode.FullyQualifiedName)
                .ThenBy(codeNode => codeNode.Id)
                .Take(candidateLimit)
                .ToArrayAsync(ct);

        if (codeNodes.Length == 0)
        {
            return new HybridSearchResult(query.SearchText, []);
        }

        var queryEmbedding = query.IncludeMemories || codeNodes.Any(static codeNode => codeNode.VectorEmbedding is { Length: > 0 })
            ? (await embeddingsService.Generate(query.SearchText, ct).ConfigureAwait(false)).Vector
            : null;

        var vectorScores = queryEmbedding is { Length: > 0 }
            ? await vectorScorer.GetScoresAsync(context,
                    query,
                    codeNodes.Select(static codeNode => codeNode.Id).ToArray(),
                    queryEmbedding,
                    ct)
                .ConfigureAwait(false)
            : new Dictionary<int, float>();
        IReadOnlyDictionary<int, MemoryNode[]> memoriesByCodeNodeId = query.IncludeMemories
            ? await memoryRepository.GetNodeMemories(
                codeNodes.Select(static codeNode => codeNode.Id).ToArray(),
                intents: null,
                ct)
            : new Dictionary<int, MemoryNode[]>();

        var rankedNodes = codeNodes.ApplyHybridScoring(
            query.SearchText,
            vectorScores,
            query.Limit,
            memoriesByCodeNodeId);

        return new HybridSearchResult(query.SearchText, HybridSearchMapper.ToSearchHit(rankedNodes));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetCandidateLimit(int limit)
        => Math.Clamp(limit * CandidateLimitMultiplier, MinimumCandidateLimit, MaximumCandidateLimit);
}
