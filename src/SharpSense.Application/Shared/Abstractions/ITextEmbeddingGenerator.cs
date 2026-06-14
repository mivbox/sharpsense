namespace SharpSense.Application.Shared.Abstractions;

public interface ITextEmbeddingGenerator
{
    Task<float[]> Generate(string text)
        => Generate(text, CancellationToken.None);

    Task<float[]> Generate(string text, CancellationToken ct = default);

    Task<IReadOnlyList<float[]>> GenerateBatch(
        IReadOnlyList<string> texts)
        => GenerateBatch(
            texts,
            CancellationToken.None);

    Task<IReadOnlyList<float[]>> GenerateBatch(
        IReadOnlyList<string> texts,
        CancellationToken ct = default);
}
