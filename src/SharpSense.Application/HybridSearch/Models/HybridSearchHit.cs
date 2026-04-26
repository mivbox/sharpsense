using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.HybridSearch.Models;

public sealed record HybridSearchHit(
    int Id,
    string CanonicalId,
    string? ProjectId,
    string FullyQualifiedName,
    string DisplayName,
    NodeType NodeType,
    string RelativeFilePath,
    int StartLine,
    int EndLine,
    string Summary);
