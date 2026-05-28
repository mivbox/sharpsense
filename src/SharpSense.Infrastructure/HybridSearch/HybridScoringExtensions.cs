using System.Runtime.CompilerServices;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.HybridSearch;

internal static class HybridScoringExtensions
{
    public static CodeNode[] ApplyHybridScoring(this IEnumerable<CodeNode> nodes,
        string searchText,
        IReadOnlyDictionary<int, float> vectorScores,
        int limit,
        IReadOnlyDictionary<int, MemoryNode[]>? memoriesByCodeNodeId = null)
    {
        var tokens = HybridSearchTokenizer.Tokenize(searchText);

        return nodes
            .Select(
                codeNode =>
                {
                    var keywordScore = ComputeKeywordScore(codeNode,
                        searchText,
                        tokens,
                        memoriesByCodeNodeId?.GetValueOrDefault(codeNode.Id) ?? []);
                    var vectorScore = vectorScores.GetValueOrDefault(codeNode.Id, 0f);
                    var totalScore = keywordScore + (vectorScore * 40f);
                    return new RankedSearchResult(codeNode, keywordScore, vectorScore, totalScore);
                })
            .Where(static result => result.KeywordScore > 0f || result.VectorScore > 0f)
            .OrderByDescending(static result => result.TotalScore)
            .ThenByDescending(static result => result.KeywordScore)
            .ThenByDescending(static result => result.VectorScore)
            .ThenBy(static result => result.Node.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static result => result.Node.Id)
            .Take(limit)
            .Select(static result => result.Node)
            .ToArray();
    }

    private static float ComputeKeywordScore(CodeNode codeNode,
        string searchText,
        IReadOnlyList<string> tokens,
        IReadOnlyList<MemoryNode> memories)
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

        if (Contains(codeNode.SearchText, searchText))
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

            if (Contains(codeNode.SearchText, token))
            {
                score += 6f;
            }

            if (Contains(codeNode.RelativeFilePath, token))
            {
                score += 4f;
            }
        }

        foreach (var memory in memories)
        {
            if (Contains(memory.Content, searchText))
            {
                score += 24f;
            }

            if (memory.Tags.Any(tag => Contains(tag, searchText)))
            {
                score += 18f;
            }

            foreach (var token in tokens)
            {
                if (Contains(memory.Content, token))
                {
                    score += 4f;
                }

                if (memory.Tags.Any(tag => Contains(tag, token)))
                {
                    score += 6f;
                }
            }
        }

        return score;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Contains(string value, string searchText)
        => value.Contains(searchText, StringComparison.OrdinalIgnoreCase);

    private sealed record RankedSearchResult(CodeNode Node, float KeywordScore, float VectorScore, float TotalScore);
}
