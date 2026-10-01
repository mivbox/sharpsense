using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Domain.KnowledgeGraph.Nodes;

public class CodeNode
{
    public int Id { get; set; }

    public string CanonicalId { get; set; } = string.Empty;

    public string? ProjectId { get; set; }

    public string FullyQualifiedName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public NodeType NodeType { get; set; } = NodeType.Class;

    public string RelativeFilePath { get; set; } = string.Empty;

    public int StartLine { get; set; }

    public int EndLine { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;

    public string? BodyHash { get; set; }

    public float[]? VectorEmbedding { get; set; }
}
