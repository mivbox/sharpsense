using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Cli.Ui.Api;

public sealed record SearchRequest(
    string Query,
    int Limit = 10,
    string? Project = null,
    NodeType[]? NodeTypes = null,
    bool IncludeMemories = false,
    string[]? Tags = null);
