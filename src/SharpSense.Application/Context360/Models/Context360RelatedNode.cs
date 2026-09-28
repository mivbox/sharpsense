namespace SharpSense.Application.Context360.Models;

public sealed record Context360RelatedNode(
    int Id,
    string Name,
    int? CodeNodeId = null);
