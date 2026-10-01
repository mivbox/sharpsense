using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Cli.Ui.Api;

public sealed record AddMemoryRequest(string Content, string[]? Tags, MemoryIntent Intent = MemoryIntent.Convention);
