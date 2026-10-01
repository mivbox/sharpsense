using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Cli.Ui.Api;

public sealed record AddMemoryResponse(bool Ok, int NodeId, MemoryIntent Intent);
