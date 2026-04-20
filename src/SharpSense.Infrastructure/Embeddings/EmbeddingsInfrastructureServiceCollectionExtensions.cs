using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharpSense.Infrastructure.Embeddings;

public static class EmbeddingsInfrastructureServiceCollectionExtensions
{
    private const string _defaultModelName = "default";
    private const string _localEmbeddingsDirectoryName = "LocalEmbeddingsModel";
    private const string _modelFileName = "model.onnx";
    private const string _vocabFileName = "vocab.txt";

    public static IServiceCollection AddEmbeddingsInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var modelDirectory = Path.Combine(
            AppContext.BaseDirectory,
            _localEmbeddingsDirectoryName,
            _defaultModelName);

        services.AddOptions<LocalEmbeddingsOptions>()
            .Configure(options =>
            {
                options.ModelPath = Path.Combine(modelDirectory, _modelFileName);
                options.VocabPath = Path.Combine(modelDirectory, _vocabFileName);
                options.Dimensions = LocalEmbeddingsOptions.DefaultDimensions;
                options.MaximumTokens = LocalEmbeddingsOptions.DefaultMaximumTokens;
                options.CaseSensitive = false;
            })
            .ValidateOnStart();

        services.TryAddSingleton<IEmbeddingGenerator, EmbeddingGenerator>();

        return services;
    }
}
