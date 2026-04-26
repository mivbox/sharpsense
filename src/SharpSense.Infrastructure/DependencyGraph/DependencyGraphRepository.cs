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
        var graphNodeIdsQuery = context.ProjectNodes
            .AsNoTracking()
            .Select(projectNode => projectNode.Id)
            .Concat(
                context.CodeNodes
                    .AsNoTracking()
                    .Select(codeNode => codeNode.CanonicalId));

        var dependencyEdges = await context.DependencyEdges
            .AsNoTracking()
            .Where(dependencyEdge =>
                graphNodeIdsQuery.Contains(dependencyEdge.CallerId) &&
                graphNodeIdsQuery.Contains(dependencyEdge.CalleeId))
            .OrderBy(dependencyEdge => dependencyEdge.CallerId)
            .ThenBy(dependencyEdge => dependencyEdge.CalleeId)
            .ThenBy(dependencyEdge => dependencyEdge.EdgeType)
            .ToArrayAsync(ct);

        return new GraphResult(
            DependencyGraphMapper.ToGraphNodes(projectNodes, codeNodes),
            DependencyGraphMapper.ToGraphEdges(dependencyEdges));
    }
}
