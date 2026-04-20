using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Features.ImpactAnalysis.Contracts;

public sealed record ImpactedCodeNode(
    string Id,
    string ProjectId,
    string FullyQualifiedName,
    NodeType NodeType,
    string RelativeFilePath,
    int StartLine,
    int EndLine,
    string Summary);
