using System.ComponentModel.DataAnnotations;

namespace SharpSense.Infrastructure.Embeddings;

public sealed class LocalEmbeddingsOptions : IValidatableObject
{
    public const int DefaultDimensions = 384;
    public const int DefaultMaximumTokens = 512;
    public const int DefaultBatchSize = 32;

    [Required]
    public string ModelPath { get; set; } = string.Empty;

    [Required]
    public string VocabPath { get; set; } = string.Empty;

    [Required]
    [Range(1, int.MaxValue)]
    public int Dimensions { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int MaximumTokens { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int BatchSize { get; set; } = DefaultBatchSize;

    public bool CaseSensitive { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Path.Exists(ModelPath) && !ModelPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("ModelPath must point to a .onnx file.", [nameof(ModelPath)]);
        }

        if (Path.Exists(VocabPath) && !VocabPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("VocabPath must point to a .txt file.", [nameof(VocabPath)]);
        }
    }
}
