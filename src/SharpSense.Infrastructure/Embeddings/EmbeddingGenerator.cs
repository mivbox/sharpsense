using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Connectors.Onnx;
using Microsoft.SemanticKernel.Embeddings;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Infrastructure.Embeddings;

internal sealed class EmbeddingGenerator : SharpSense.Application.Shared.Abstractions.IEmbeddingGenerator, IDisposable
{
    private readonly LocalEmbeddingsOptions _options;
    private readonly Lazy<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerator;
    private bool _disposed;

    public EmbeddingGenerator(IOptions<LocalEmbeddingsOptions> options)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));

        _embeddingGenerator = new Lazy<IEmbeddingGenerator<string, Embedding<float>>>(
            CreateEmbeddingGenerator,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public EmbeddingGenerator(
        LocalEmbeddingsOptions options,
        Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        ArgumentNullException.ThrowIfNull(embeddingGenerator);

        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (_options.MaximumTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumTokens must be greater than zero.");
        }

        if (_options.BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "BatchSize must be greater than zero.");
        }

        if (_options.Dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Dimensions must be greater than zero.");
        }

        _embeddingGenerator = new Lazy<IEmbeddingGenerator<string, Embedding<float>>>(
            () => embeddingGenerator,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<TextEmbedding> Generate(string text, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var embedding = await _embeddingGenerator.Value.GenerateAsync(text, cancellationToken: ct);

        return new TextEmbedding(text, embedding.Vector.ToArray());
    }

    public async Task<IReadOnlyList<TextEmbedding>> GenerateBatch(
        IEnumerable<string> texts,
        IProgress<EmbeddingGenerationProgress>? progress,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(texts);

        var normalizedTexts = texts.Select(ValidateText)
            .ToArray();
        if (normalizedTexts.Length == 0)
        {
            return Array.Empty<TextEmbedding>();
        }

        var embeddings = new List<TextEmbedding>(normalizedTexts.Length);
        var completedItems = 0;
        var batches = normalizedTexts.Chunk(_options.BatchSize)
            .ToArray();

        for (var batchIndex = 0; batchIndex < batches.Length; batchIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var batch = batches[batchIndex];
            progress?.Report(
                new EmbeddingGenerationProgress(
                    BuildProgressMessage(completedItems, normalizedTexts.Length, batchIndex, batches.Length),
                    completedItems,
                    normalizedTexts.Length));
            var generatedEmbeddings = await _embeddingGenerator.Value.GenerateAndZipAsync(batch, cancellationToken: ct);

            foreach (var (text, embedding) in generatedEmbeddings)
            {
                ct.ThrowIfCancellationRequested();

                embeddings.Add(new TextEmbedding(text, embedding.Vector.ToArray()));
                completedItems++;
                progress?.Report(
                    new EmbeddingGenerationProgress(
                        BuildProgressMessage(completedItems, normalizedTexts.Length, batchIndex, batches.Length),
                        completedItems,
                        normalizedTexts.Length));
            }
        }

        return embeddings;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_embeddingGenerator.IsValueCreated && _embeddingGenerator.Value is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _disposed = true;
    }

    private IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator()
    {
        if (string.IsNullOrWhiteSpace(_options.ModelPath))
        {
            throw new InvalidOperationException($"{nameof(LocalEmbeddingsOptions.ModelPath)} must be configured before generating embeddings.");
        }

        if (string.IsNullOrWhiteSpace(_options.VocabPath))
        {
            throw new InvalidOperationException($"{nameof(LocalEmbeddingsOptions.VocabPath)} must be configured before generating embeddings.");
        }

        if (!File.Exists(_options.ModelPath))
        {
            throw new FileNotFoundException(
                $"The configured ONNX model file does not exist: {_options.ModelPath}",
                _options.ModelPath);
        }

        if (!File.Exists(_options.VocabPath))
        {
            throw new FileNotFoundException(
                $"The configured vocab file does not exist: {_options.VocabPath}",
                _options.VocabPath);
        }

#pragma warning disable CS0618
        var service = BertOnnxTextEmbeddingGenerationService.Create(
            _options.ModelPath,
            _options.VocabPath,
            new BertOnnxOptions
            {
                CaseSensitive = _options.CaseSensitive,
                MaximumTokens = _options.MaximumTokens,
                NormalizeEmbeddings = true,
                PoolingMode = EmbeddingPoolingMode.Mean
            });

        return service.AsEmbeddingGenerator<string, float>();
    }

    private static string ValidateText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return text;
    }

    private static string BuildProgressMessage(
        int completedItems,
        int totalItems,
        int batchIndex,
        int batchCount)
        => batchCount <= 1
            ? $"Generating embeddings {completedItems}/{totalItems}..."
            : $"Generating embeddings {completedItems}/{totalItems} (batch {batchIndex + 1}/{batchCount})...";
}
