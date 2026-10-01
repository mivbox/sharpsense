using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;

namespace SharpSense.IntegrationTests.Memory;

internal sealed class NoopEmbeddingGenerator : IEmbeddingGenerator
{
    public Task<TextEmbedding> Generate(string text, CancellationToken ct = default)
        => Task.FromResult(new TextEmbedding(text, Array.Empty<float>()));

    public Task<IReadOnlyList<TextEmbedding>> GenerateBatch(
        IEnumerable<string> texts,
        IProgress<EmbeddingGenerationProgress>? progress,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TextEmbedding>>(
            texts
                .Select(text => new TextEmbedding(text, Array.Empty<float>()))
                .ToArray());
}
