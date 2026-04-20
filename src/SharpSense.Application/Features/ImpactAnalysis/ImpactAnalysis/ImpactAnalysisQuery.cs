using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Features.ImpactAnalysis.ImpactAnalysis;

public sealed record ImpactAnalysisQuery(
    string Identifier,
    int MaxDepth = 3,
    bool IncludeTransitive = true,
    EdgeType[]? IncludedEdgeTypes = null);
