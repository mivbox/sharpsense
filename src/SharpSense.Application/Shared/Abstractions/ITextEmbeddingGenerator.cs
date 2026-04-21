namespace SharpSense.Application.Shared.Abstractions;

public interface ITextEmbeddingGenerator
{
    Task<float[]> Generate(string text, CancellationToken ct = default);

    Task<IReadOnlyList<float[]>> GenerateBatch(
        IReadOnlyList<string> texts,
        CancellationToken ct = default);
}
