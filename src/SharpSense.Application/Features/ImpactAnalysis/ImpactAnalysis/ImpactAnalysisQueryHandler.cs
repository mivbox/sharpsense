using SharpSense.Application.Features.ImpactAnalysis.Contracts;
using SharpSense.Application.Features.ImpactAnalysis.Infrastructure;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Features.ImpactAnalysis.ImpactAnalysis;

public sealed class ImpactAnalysisQueryHandler(IImpactAnalysisService impactAnalysisService)
    : IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult>
{
    public Task<ImpactAnalysisResult> Handle(ImpactAnalysisQuery query, CancellationToken ct)
        => impactAnalysisService.Analyze(query, ct);
}
