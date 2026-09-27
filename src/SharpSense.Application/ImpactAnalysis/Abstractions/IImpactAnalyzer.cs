using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Models;

namespace SharpSense.Application.ImpactAnalysis.Abstractions;

public interface IImpactAnalyzer
{
    Task<ImpactAnalysisResult> Analyze(ImpactAnalysisQuery query, CancellationToken ct);
}
