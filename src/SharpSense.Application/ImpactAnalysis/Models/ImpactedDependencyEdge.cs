using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.ImpactAnalysis.Models;

public sealed record ImpactedDependencyEdge(
    string CallerId,
    string CalleeId,
    EdgeType EdgeType);
