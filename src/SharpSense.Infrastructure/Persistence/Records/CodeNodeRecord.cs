using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.Persistence.Records;

public sealed class CodeNodeRecord
{
    public int Id { get; set; }

    public int? ProjectNodeId { get; set; }

    public int DocumentId { get; set; }

    public string FullyQualifiedName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public NodeType NodeType { get; set; } = NodeType.Class;

    public int StartLine { get; set; }

    public int EndLine { get; set; }

    public string Summary { get; set; } = string.Empty;

    public float[]? VectorEmbedding { get; set; }
}
