using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Cli.Ui.Api;

public sealed record AddMemoryResponse(
    bool Ok,
    int NodeId,
    Guid MemoryId,
    MemoryIntent Intent,
    bool IsStale);
