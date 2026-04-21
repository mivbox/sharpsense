using JetBrains.Annotations;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Features.Indexing.Contracts;

[PublicAPI]
public sealed record IndexedCodeNode(
    string Id,
    string? ProjectId,
    string FullyQualifiedName,
    NodeType NodeType,
    string RelativeFilePath,
    int StartLine,
    int EndLine,
    string Summary);
