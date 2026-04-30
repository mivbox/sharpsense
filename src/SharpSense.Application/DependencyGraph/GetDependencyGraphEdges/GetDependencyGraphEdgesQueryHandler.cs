using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.GetDependencyGraphEdges.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Application.DependencyGraph.GetDependencyGraphEdges;

[ExcludeFromCodeCoverage(Justification = "Application layer proxy, no logic to test.")]
public sealed class GetDependencyGraphEdgesQueryHandler(IDependencyGraphRepository dependencyGraphRepository)
    : IQueryHandler<GetDependencyGraphEdgesQuery, IAsyncEnumerable<GraphEdge>>
{
    public Task<IAsyncEnumerable<GraphEdge>> Handle(
        GetDependencyGraphEdgesQuery query,
        CancellationToken ct)
        => Task.FromResult(dependencyGraphRepository.GetGraphEdges(query.DirectoryIds, ct));
}
