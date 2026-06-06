namespace SharpSense.Application.Memory.GetNodeMemories.Models;

using SharpSense.Domain.KnowledgeGraph.Enums;

public sealed record GetNodeMemoriesQuery(
    int NodeId,
    MemoryIntent[]? IntentFilter = null);
