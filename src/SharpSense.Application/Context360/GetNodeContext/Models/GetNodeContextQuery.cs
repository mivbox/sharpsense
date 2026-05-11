namespace SharpSense.Application.Context360.GetNodeContext.Models;

public sealed record GetNodeContextQuery(
    int NodeId,
    int MaxRelated);
