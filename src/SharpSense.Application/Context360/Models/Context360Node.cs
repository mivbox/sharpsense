using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Context360.Models;

public sealed record Context360Node(
    int Id,
    string Name,
    NodeType Kind,
    string RelativeFilePath,
    int StartLine,
    int EndLine);
