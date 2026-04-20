using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Shared.Models;

public sealed record CodeNodeResult(
    string Id,
    string ProjectId,
    string FullyQualifiedName,
    NodeType NodeType,
    string RelativeFilePath,
    int StartLine,
    int EndLine,
    string Summary);
