using JetBrains.Annotations;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Indexing.Models;

[PublicAPI]
public sealed record IndexedCodeNode(
    string CanonicalId,
    string? ProjectId,
    string FullyQualifiedName,
    string DisplayName,
    NodeType NodeType,
    string RelativeFilePath,
    int StartLine,
    int EndLine,
    string Summary,
    string SearchText,
    string? BodyHash = null,
    float[]? VectorEmbedding = null);
