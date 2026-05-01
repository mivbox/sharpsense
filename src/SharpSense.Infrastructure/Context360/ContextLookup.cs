using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Shared;

namespace SharpSense.Infrastructure.Context360;

public sealed class ContextLookup(IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : IContextLookup
{
    public async Task<Context360LookupResult?> GetNodeContext(
        int nodeId,
        int maxRelated,
        CancellationToken ct)
    {
        if (nodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId), nodeId, "NodeId must be greater than zero.");
        }

        if (maxRelated <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRelated), maxRelated, "maxRelated must be greater than zero.");
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var targetNode = await CodeNodeNavigationQueries.ProjectCodeNodeResults(
                context,
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => codeNode.Id == nodeId))
            .FirstOrDefaultAsync(ct);

        if (targetNode is null)
        {
            return null;
        }

        var callers = await GetRelatedNodes(
            context,
            context.DependencyEdges
                .AsNoTracking()
                .Where(edge =>
                    edge.CalleeNodeId == nodeId &&
                    edge.EdgeType != EdgeType.Implements),
            selectCaller: true,
            maxRelated,
            ct);
        var implementers = await GetRelatedNodes(
            context,
            context.DependencyEdges
                .AsNoTracking()
                .Where(edge =>
                    edge.CalleeNodeId == nodeId &&
                    edge.EdgeType == EdgeType.Implements),
            selectCaller: true,
            maxRelated,
            ct);
        var callees = await GetRelatedNodes(
            context,
            context.DependencyEdges
                .AsNoTracking()
                .Where(edge =>
                    edge.CallerNodeId == nodeId &&
                    edge.EdgeType != EdgeType.Implements),
            selectCaller: false,
            maxRelated,
            ct);
        var inherits = await GetRelatedNodes(
            context,
            context.DependencyEdges
                .AsNoTracking()
                .Where(edge =>
                    edge.CallerNodeId == nodeId &&
                    edge.EdgeType == EdgeType.Implements),
            selectCaller: false,
            maxRelated,
            ct);

        return new Context360LookupResult(
            targetNode,
            callers,
            implementers,
            callees,
            inherits);
    }
    private static async Task<CodeNodeResult[]> GetRelatedNodes(
        SharpSenseDbContext context,
        IQueryable<DependencyEdgeRecord> edgeQuery,
        bool selectCaller,
        int maxRelated,
        CancellationToken ct)
    {
        var relatedNodeIds = (selectCaller
            ? edgeQuery
                .Select(static edge => edge.CallerNodeId)
            : edgeQuery
                .Select(static edge => edge.CalleeNodeId)
            )
            .Distinct();

        return await CodeNodeNavigationQueries.ProjectCodeNodeResults(
                context,
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => relatedNodeIds.Contains(codeNode.Id))
                    .OrderBy(static codeNode => codeNode.FullyQualifiedName)
                    .ThenBy(static codeNode => codeNode.Id)
                    .Take(maxRelated))
            .ToArrayAsync(ct);
    }
}
