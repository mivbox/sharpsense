using SharpSense.Application.Features.ImpactAnalysis.Contracts;
using SharpSense.Application.Features.ImpactAnalysis.ImpactAnalysis;

namespace SharpSense.Application.Features.ImpactAnalysis.Infrastructure;

public interface IImpactAnalyzer
{
    Task<ImpactAnalysisResult> Analyze(ImpactAnalysisQuery query, CancellationToken ct);
}
