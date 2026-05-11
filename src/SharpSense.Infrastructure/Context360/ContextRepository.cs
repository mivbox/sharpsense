using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.Context360.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Context360;

public sealed class ContextRepository(IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : IContextRepository
{
    public async Task<Context360Result?> GetNodeContext(
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
        var targetNode = await (
            from codeNode in context.CodeNodes.AsNoTracking()
            join document in context.Documents.AsNoTracking() on codeNode.DocumentId equals document.Id
            where codeNode.Id == nodeId
            select new Context360Node(
                codeNode.Id,
                codeNode.DisplayName,
                codeNode.NodeType,
                document.RelativePath,
                codeNode.StartLine,
                codeNode.EndLine))
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

        return new Context360Result(
            targetNode,
            callers,
            implementers,
            callees,
            inherits);
    }

    private static Task<Context360RelatedNode[]> GetRelatedNodes(
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

        return context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => relatedNodeIds.Contains(codeNode.Id))
            .OrderBy(static codeNode => codeNode.FullyQualifiedName)
            .ThenBy(static codeNode => codeNode.Id)
            .Take(maxRelated)
            .Select(static codeNode => new Context360RelatedNode(
                codeNode.Id,
                codeNode.NodeType == NodeType.Method && codeNode.DisplayName.Contains("(")
                    ? codeNode.DisplayName.Substring(0, codeNode.DisplayName.IndexOf("("))
                    : codeNode.DisplayName))
            .ToArrayAsync(ct);
    }
}
