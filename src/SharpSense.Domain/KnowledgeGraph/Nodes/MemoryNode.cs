namespace SharpSense.Domain.KnowledgeGraph.Nodes;

public sealed record MemoryNode(
    Guid Id,
    string TargetFullyQualifiedName,
    string TargetCodeHash,
    string Content,
    string ContentHash,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    bool IsStale);
