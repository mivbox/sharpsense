namespace SharpSense.Application.Memory.AttachMemory.Models;

using SharpSense.Domain.KnowledgeGraph.Enums;

public sealed record AttachMemoryCommand(
    int NodeId,
    string Content,
    string[]? Tags,
    MemoryIntent Intent = MemoryIntent.Convention);
