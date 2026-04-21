using SharpSense.Application.Features.ImpactAnalysis.Contracts;
using SharpSense.Application.Features.ImpactAnalysis.Infrastructure;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Features.ImpactAnalysis.ImpactAnalysis;

public sealed class ImpactAnalysisQueryHandler(IImpactAnalyzer impactAnalyzer)
    : IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult>
{
    public Task<ImpactAnalysisResult> Handle(ImpactAnalysisQuery query, CancellationToken ct)
        => impactAnalyzer.Analyze(query, ct);
}
