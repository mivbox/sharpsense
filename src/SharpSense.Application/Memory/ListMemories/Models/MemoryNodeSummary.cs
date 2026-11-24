using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Memory.ListMemories.Models;

/// <summary>
/// Summarizes a persisted semantic memory and its owning indexed code node without transferring full Markdown content.
/// </summary>
public sealed record MemoryNodeSummary(
    Guid Id,
    int TargetCodeNodeId,
    string TargetFullyQualifiedName,
    string TargetRelativeFilePath,
    MemoryIntent Intent,
    string[] Tags,
    bool IsStale,
    DateTimeOffset CreatedAt,
    string ContentPreview);
