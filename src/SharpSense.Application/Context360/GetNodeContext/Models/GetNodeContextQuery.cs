using SharpSense.Application.Context360.Models;

namespace SharpSense.Application.Context360.GetNodeContext.Models;

public sealed record GetNodeContextQuery(
    int NodeId,
    int MaxRelated,
    ContextSection[]? IncludedSections = null);
