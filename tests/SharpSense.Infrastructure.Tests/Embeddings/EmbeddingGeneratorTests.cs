using AwesomeAssertions;
using Microsoft.Extensions.AI;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Embeddings;

namespace SharpSense.Infrastructure.Tests.Embeddings;

public sealed class EmbeddingGeneratorTests
{
    [Fact]
    public async Task WhenGenerateBatchExceedsConfiguredBatchSize_ThenItProcessesMultipleBatchesAndReportsOverallProgress()
    {
        var innerGenerator = new RecordingEmbeddingGenerator();
        using var generator = new EmbeddingGenerator(
            new LocalEmbeddingsOptions
            {
                ModelPath = "model.onnx",
                VocabPath = "vocab.txt",
                Dimensions = LocalEmbeddingsOptions.DefaultDimensions,
                MaximumTokens = LocalEmbeddingsOptions.DefaultMaximumTokens,
                BatchSize = 2
            },
            innerGenerator);
        var progress = new CollectingProgress();
        var inputs = new[] { "alpha", "beta", "gamma", "delta", "epsilon" };

        var embeddings = await generator.GenerateBatch(inputs, progress, TestContext.Current.CancellationToken);

        innerGenerator.RequestedBatches.Should()
            .HaveCount(3).And.SatisfyRespectively(
                batch => batch.Should().Equal("alpha", "beta"),
                batch => batch.Should().Equal("gamma", "delta"),
                batch => batch.Should().Equal("epsilon"));
        embeddings
            .Select(static embedding => embedding.Text)
            .Should()
            .Equal(inputs);
        embeddings.Should().SatisfyRespectively(
            embedding => embedding.Vector.Should().Equal(1f),
            embedding => embedding.Vector.Should().Equal(2f),
            embedding => embedding.Vector.Should().Equal(3f),
            embedding => embedding.Vector.Should().Equal(4f),
            embedding => embedding.Vector.Should().Equal(5f));
        progress.Updates
            .Select(static update => update.CompletedItems)
            .Should()
            .ContainInOrder(0, 1, 2, 2, 3, 4, 4, 5);
        progress.Updates.Should()
            .Contain(update => update.CurrentTask.Contains("batch 2/3", StringComparison.Ordinal)).And.Contain(update => update.CurrentTask.Contains(
                    "5/5",
                    StringComparison.Ordinal));
    }

    private sealed class CollectingProgress : IProgress<EmbeddingGenerationProgress>
    {
        public List<EmbeddingGenerationProgress> Updates { get; } = [];

        public void Report(EmbeddingGenerationProgress value)
            => Updates.Add(value);
    }

    private sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<string[]> RequestedBatches { get; } = [];

        public void Dispose()
        {
        }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var batch = values.ToArray();
            RequestedBatches.Add(batch);

            return Task.FromResult(
                new GeneratedEmbeddings<Embedding<float>>(
                    batch.Select(value => new Embedding<float>(new[]
                    {
                        value switch
                        {
                            "alpha" => 1f,
                            "beta" => 2f,
                            "gamma" => 3f,
                            "delta" => 4f,
                            "epsilon" => 5f,
                            _ => throw new InvalidOperationException("Unexpected embedding input.")
                        }
                    }))));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;
    }
}
