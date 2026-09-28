using SharpSense.Application.ImpactAnalysis.Abstractions;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.ImpactAnalysis.ImpactAnalysis;

internal sealed class ImpactAnalysisQueryHandler(IImpactAnalyzer impactAnalyzer)
    : IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult>
{
    public Task<ImpactAnalysisResult> Handle(ImpactAnalysisQuery query, CancellationToken ct)
        => impactAnalyzer.Analyze(query, ct);
}
