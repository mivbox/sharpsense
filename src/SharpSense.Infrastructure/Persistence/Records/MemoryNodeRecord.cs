namespace SharpSense.Infrastructure.Persistence.Records;

public sealed class MemoryNodeRecord
{
    public Guid Id { get; set; }

    public string TargetFullyQualifiedName { get; set; } = string.Empty;

    public string TargetCodeHash { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public string TagsJson { get; set; } = "[]";

    public string Intent { get; set; } = "Convention";

    public float[]? VectorEmbedding { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
