using SharpSense.Application.Shared.Models;

namespace SharpSense.Infrastructure.Embeddings;

public interface IEmbeddingGenerator
{
    Task<TextEmbedding> Generate(string text, CancellationToken ct = default);

    Task<IReadOnlyList<TextEmbedding>> GenerateBatch(
        IEnumerable<string> texts,
        IProgress<EmbeddingGenerationProgress>? progress,
        CancellationToken ct = default);
}
