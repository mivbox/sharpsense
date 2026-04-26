using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Features.ImpactAnalysis.Contracts;

public sealed record ImpactedCodeNode(
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
