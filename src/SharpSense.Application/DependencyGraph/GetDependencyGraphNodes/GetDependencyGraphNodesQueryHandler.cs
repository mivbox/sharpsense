using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.GetDependencyGraphNodes.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Application.DependencyGraph.GetDependencyGraphNodes;

[ExcludeFromCodeCoverage(Justification = "Application layer proxy, no logic to test.")]
public sealed class GetDependencyGraphNodesQueryHandler(IDependencyGraphRepository dependencyGraphRepository)
    : IQueryHandler<GetDependencyGraphNodesQuery, IAsyncEnumerable<GraphNode>>
{
    public Task<IAsyncEnumerable<GraphNode>> Handle(
        GetDependencyGraphNodesQuery query,
        CancellationToken ct)
        => Task.FromResult(dependencyGraphRepository.GetGraphNodes(query.DirectoryIds, ct));
}
