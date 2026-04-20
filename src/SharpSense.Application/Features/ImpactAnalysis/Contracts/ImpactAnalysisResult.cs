namespace SharpSense.Application.Features.ImpactAnalysis.Contracts;

public sealed record ImpactAnalysisResult(
    string TargetSymbol,
    ImpactedCodeNode[] ImpactedNodes,
    ImpactedDependencyEdge[] Dependencies);
