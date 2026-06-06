namespace SharpSense.Domain.KnowledgeGraph.Nodes;

using SharpSense.Domain.KnowledgeGraph.Enums;

public sealed record MemoryNode(
    Guid Id,
    string TargetFullyQualifiedName,
    string TargetCodeHash,
    string Content,
    string ContentHash,
    IReadOnlyList<string> Tags,
    MemoryIntent Intent,
    DateTimeOffset CreatedAt,
    bool IsStale);
