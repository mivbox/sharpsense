namespace SharpSense.Application.ImpactAnalysis.Models;

public sealed record ImpactAnalysisResult(
    string TargetSymbol,
    ImpactedCodeNode[] ImpactedNodes,
    ImpactedDependencyEdge[] Dependencies);
