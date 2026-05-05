using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Indexing;

internal static class CodeNodeEmbeddingCoordinator
{
    public static async Task<IReadOnlyList<IndexedCodeNode>> Populate(
        IReadOnlyList<IndexedCodeNode> codeNodes,
        IReadOnlyList<IndexedCodeNode> persistedCodeNodes,
        bool skipEmbeddings,
        IEmbeddingGenerator embeddingGenerator,
        IProgress<EmbeddingGenerationProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(codeNodes);
        ArgumentNullException.ThrowIfNull(persistedCodeNodes);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);

        if (codeNodes.Count == 0)
        {
            return [];
        }

        var persistedCodeNodesByCanonicalId = persistedCodeNodes.ToDictionary(
            static codeNode => codeNode.CanonicalId,
            StringComparer.Ordinal);
        var updatedCodeNodes = new IndexedCodeNode[codeNodes.Count];
        var embeddingRequests = new List<(int Index, string SearchText)>();

        for (var index = 0; index < codeNodes.Count; index++)
        {
            var codeNode = codeNodes[index];
            if (persistedCodeNodesByCanonicalId.TryGetValue(codeNode.CanonicalId, out var persistedCodeNode) &&
                CanReuseEmbedding(codeNode, persistedCodeNode))
            {
                updatedCodeNodes[index] = codeNode with
                {
                    VectorEmbedding = persistedCodeNode.VectorEmbedding
                };
                continue;
            }

            if (skipEmbeddings)
            {
                updatedCodeNodes[index] = codeNode with
                {
                    VectorEmbedding = null
                };
                continue;
            }

            embeddingRequests.Add((index, codeNode.SearchText));
            updatedCodeNodes[index] = codeNode;
        }

        if (embeddingRequests.Count == 0)
        {
            return updatedCodeNodes;
        }

        var embeddings = await embeddingGenerator.GenerateBatch(
            embeddingRequests.Select(static request => request.SearchText),
            progress,
            ct);
        if (embeddings.Count != embeddingRequests.Count)
        {
            throw new InvalidOperationException("The embeddings generator returned an unexpected number of vectors.");
        }

        for (var index = 0; index < embeddingRequests.Count; index++)
        {
            var request = embeddingRequests[index];
            updatedCodeNodes[request.Index] = updatedCodeNodes[request.Index] with
            {
                VectorEmbedding = embeddings[index].Vector
            };
        }

        return updatedCodeNodes;
    }

    private static bool CanReuseEmbedding(
        IndexedCodeNode codeNode,
        IndexedCodeNode persistedCodeNode)
    {
        return persistedCodeNode.VectorEmbedding is { Length: > 0 } &&
               string.Equals(codeNode.SearchText, persistedCodeNode.SearchText, StringComparison.Ordinal) &&
               string.Equals(codeNode.BodyHash, persistedCodeNode.BodyHash, StringComparison.Ordinal);
    }
}
