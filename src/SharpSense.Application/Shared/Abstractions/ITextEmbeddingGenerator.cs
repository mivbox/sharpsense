namespace SharpSense.Application.Shared.Abstractions;

public interface ITextEmbeddingGenerator
{
    Task<float[]> GenerateAsync(string text, CancellationToken ct = default);

    Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default);
}
