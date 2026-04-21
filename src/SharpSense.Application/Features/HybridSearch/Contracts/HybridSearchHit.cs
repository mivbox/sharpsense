using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Features.HybridSearch.Contracts;

public sealed record HybridSearchHit(
    string Id,
    string? ProjectId,
    string FullyQualifiedName,
    NodeType NodeType,
    string RelativeFilePath,
    int StartLine,
    int EndLine,
    string Summary);
