using SharpSense.Application.Features.DependencyGraph.Contracts;
using SharpSense.Application.Features.DependencyGraph.Infrastructure;
using SharpSense.Application.Shared.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Application.Features.DependencyGraph.GetDependencyGraph;

[ExcludeFromCodeCoverage(Justification = "Application layer proxy, no logic to test.")]
public sealed class GetDependencyGraphQueryHandler(IDependencyGraphRepository dependencyGraphRepository)
    : IQueryHandler<GetDependencyGraphQuery, GraphResult>
{
    public Task<GraphResult> Handle(GetDependencyGraphQuery query, CancellationToken ct)
        => dependencyGraphRepository.GetGraph(ct);
}
