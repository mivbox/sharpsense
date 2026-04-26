using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.ImpactAnalysis.Models;

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
