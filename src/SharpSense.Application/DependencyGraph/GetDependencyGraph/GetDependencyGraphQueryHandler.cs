using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.GetDependencyGraph.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Application.DependencyGraph.GetDependencyGraph;

[ExcludeFromCodeCoverage(Justification = "Application layer proxy, no logic to test.")]
public sealed class GetDependencyGraphQueryHandler(IDependencyGraphRepository dependencyGraphRepository)
    : IQueryHandler<GetDependencyGraphQuery, GraphResult>
{
    public Task<GraphResult> Handle(GetDependencyGraphQuery query, CancellationToken ct)
        => dependencyGraphRepository.GetGraph(ct);
}
