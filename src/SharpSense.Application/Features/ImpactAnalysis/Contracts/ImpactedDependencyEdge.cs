using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Features.ImpactAnalysis.Contracts;

public sealed record ImpactedDependencyEdge(
    string CallerId,
    string CalleeId,
    EdgeType EdgeType);
