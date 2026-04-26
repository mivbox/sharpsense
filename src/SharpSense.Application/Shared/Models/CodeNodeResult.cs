using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Shared.Models;

public sealed record CodeNodeResult(
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
