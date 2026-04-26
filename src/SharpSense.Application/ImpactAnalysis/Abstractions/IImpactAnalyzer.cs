using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;

namespace SharpSense.Application.ImpactAnalysis.Abstractions;

public interface IImpactAnalyzer
{
    Task<ImpactAnalysisResult> Analyze(ImpactAnalysisQuery query, CancellationToken ct);
}
