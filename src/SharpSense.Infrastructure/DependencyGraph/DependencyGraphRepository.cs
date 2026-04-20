using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Features.DependencyGraph.Contracts;
using SharpSense.Application.Features.DependencyGraph.Infrastructure;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.DependencyGraph;

public sealed class DependencyGraphRepository(
    SharpSenseDbContext context)
    : IDependencyGraphRepository
{
    public async Task<GraphResult> GetGraph(CancellationToken ct)
    {
        var projectNodes = await context.ProjectNodes
            .AsNoTracking()
            .OrderBy(projectNode => projectNode.Name)
            .ThenBy(projectNode => projectNode.Id)
            .ToArrayAsync(ct);

        var codeNodes = await context.CodeNodes
            .AsNoTracking()
            .OrderBy(codeNode => codeNode.FullyQualifiedName)
            .ThenBy(codeNode => codeNode.Id)
            .ToArrayAsync(ct);

        var graphNodeIds = projectNodes
            .Select(projectNode => projectNode.Id)
            .Concat(codeNodes.Select(codeNode => codeNode.Id))
            .ToHashSet(StringComparer.Ordinal);

        var dependencyEdges = (await context.DependencyEdges
                .AsNoTracking()
                .ToArrayAsync(ct))
            .Where(dependencyEdge =>
                graphNodeIds.Contains(dependencyEdge.CallerId) &&
                graphNodeIds.Contains(dependencyEdge.CalleeId))
            .OrderBy(dependencyEdge => dependencyEdge.CallerId, StringComparer.Ordinal)
            .ThenBy(dependencyEdge => dependencyEdge.CalleeId, StringComparer.Ordinal)
            .ThenBy(dependencyEdge => dependencyEdge.EdgeType)
            .ToArray();

        return new GraphResult(
            DependencyGraphMapper.ToGraphNodes(projectNodes, codeNodes),
            DependencyGraphMapper.ToGraphEdges(dependencyEdges));
    }
}
