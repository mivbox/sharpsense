using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.Context360.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Context360;

internal sealed class ContextRepository(IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : IContextRepository
{
    private static readonly ContextSection[] _allSections = Enum.GetValues<ContextSection>();

    public async Task<Context360Result?> GetNodeContext(
        int nodeId,
        int maxRelated,
        IReadOnlyCollection<ContextSection>? includedSections,
        CancellationToken ct)
    {
        if (nodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId), nodeId, "NodeId must be greater than zero.");
        }

        if (maxRelated <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRelated),
                maxRelated,
                "maxRelated must be greater than zero.");
        }

        var activeSections = includedSections is { Count: > 0 }
            ? includedSections
                .Distinct()
                .OrderBy(static section => section)
                .ToArray()
            : _allSections;
        var sectionSet = activeSections.ToHashSet();

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

        var callers = sectionSet.Contains(ContextSection.Callers)
            ? await GetRelatedBucket(
                context,
                context.DependencyEdges
                    .AsNoTracking()
                    .Where(edge =>
                        edge.CalleeNodeId == nodeId &&
                        edge.EdgeType != EdgeType.Implements &&
                        edge.EdgeType != EdgeType.ParentOf),
                selectCaller: true,
                maxRelated,
                ct)
            : RelatedBucket.Empty;
        var implementers = sectionSet.Contains(ContextSection.Implementers)
            ? await GetRelatedBucket(
                context,
                context.DependencyEdges
                    .AsNoTracking()
                    .Where(edge =>
                        edge.CalleeNodeId == nodeId &&
                        edge.EdgeType == EdgeType.Implements),
                selectCaller: true,
                maxRelated,
                ct)
            : RelatedBucket.Empty;
        var callees = sectionSet.Contains(ContextSection.Callees)
            ? await GetRelatedBucket(
                context,
                context.DependencyEdges
                    .AsNoTracking()
                    .Where(edge =>
                        edge.CallerNodeId == nodeId &&
                        edge.EdgeType != EdgeType.Implements &&
                        edge.EdgeType != EdgeType.ParentOf),
                selectCaller: false,
                maxRelated,
                ct)
            : RelatedBucket.Empty;
        var inherits = sectionSet.Contains(ContextSection.Inherits)
            ? await GetRelatedBucket(
                context,
                context.DependencyEdges
                    .AsNoTracking()
                    .Where(edge =>
                        edge.CallerNodeId == nodeId &&
                        edge.EdgeType == EdgeType.Implements),
                selectCaller: false,
                maxRelated,
                ct)
            : RelatedBucket.Empty;
        var parents = sectionSet.Contains(ContextSection.Parents)
            ? await GetStructuralRelatedBucket(
                context,
                context.DependencyEdges
                    .AsNoTracking()
                    .Where(edge =>
                        edge.CalleeNodeId == nodeId &&
                        edge.EdgeType == EdgeType.ParentOf),
                selectCaller: true,
                maxRelated,
                ct)
            : RelatedBucket.Empty;
        var children = sectionSet.Contains(ContextSection.Children)
            ? await GetStructuralRelatedBucket(
                context,
                context.DependencyEdges
                    .AsNoTracking()
                    .Where(edge =>
                        edge.CallerNodeId == nodeId &&
                        edge.EdgeType == EdgeType.ParentOf),
                selectCaller: false,
                maxRelated,
                ct)
            : RelatedBucket.Empty;

        return new Context360Result(
            targetNode,
            callers.Items,
            implementers.Items,
            callees.Items,
            inherits.Items,
            parents.Items,
            children.Items,
            new Context360Totals(
                callers.Total,
                implementers.Total,
                callees.Total,
                inherits.Total,
                parents.Total,
                children.Total),
            activeSections);
    }

    private static async Task<RelatedBucket> GetRelatedBucket(
        SharpSenseDbContext context,
        IQueryable<DependencyEdgeRecord> edgeQuery,
        bool selectCaller,
        int maxRelated,
        CancellationToken ct)
    {
        var relatedNodeIds = (selectCaller
            ? edgeQuery.Select(static edge => edge.CallerNodeId)
            : edgeQuery.Select(static edge => edge.CalleeNodeId))
            .Distinct();
        var matchingNodes = context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => relatedNodeIds.Contains(codeNode.Id));

        var probe = await (
            from codeNode in matchingNodes
            join document in context.Documents.AsNoTracking() on codeNode.DocumentId equals document.Id
            orderby codeNode.FullyQualifiedName, codeNode.Id
            select new Context360RelatedNode(
                codeNode.Id,
                codeNode.NodeType == NodeType.Method && codeNode.DisplayName.Contains("(")
                    ? codeNode.DisplayName.Substring(0, codeNode.DisplayName.IndexOf("("))
                    : codeNode.DisplayName,
                codeNode.Id,
                document.RelativePath,
                codeNode.StartLine))
            .Take(maxRelated + 1)
            .ToArrayAsync(ct);

        if (probe.Length <= maxRelated)
        {
            return new RelatedBucket(probe, probe.Length);
        }

        var total = await matchingNodes.CountAsync(ct);

        return new RelatedBucket(probe[..maxRelated], total);
    }

    private static async Task<RelatedBucket> GetStructuralRelatedBucket(
        SharpSenseDbContext context,
        IQueryable<DependencyEdgeRecord> edgeQuery,
        bool selectCaller,
        int maxRelated,
        CancellationToken ct)
    {
        var relatedNodeIds = (selectCaller
            ? edgeQuery.Select(static edge => edge.CallerNodeId)
            : edgeQuery.Select(static edge => edge.CalleeNodeId))
            .Distinct();

        var codeNodes =
            from codeNode in context.CodeNodes.AsNoTracking()
            join document in context.Documents.AsNoTracking() on codeNode.DocumentId equals document.Id
            where relatedNodeIds.Contains(codeNode.Id)
            select new
            {
                codeNode.Id,
                Name = codeNode.NodeType == NodeType.Method && codeNode.DisplayName.Contains("(")
                    ? codeNode.DisplayName.Substring(0, codeNode.DisplayName.IndexOf("("))
                    : codeNode.DisplayName,
                CodeNodeId = (int?)codeNode.Id,
                RelativeFilePath = (string?)document.RelativePath,
                StartLine = (int?)codeNode.StartLine
            };
        var projectNodes = context.ProjectNodes
            .AsNoTracking()
            .Where(projectNode => relatedNodeIds.Contains(projectNode.Id))
            .Select(static projectNode => new
            {
                projectNode.Id,
                Name = projectNode.Name,
                CodeNodeId = (int?)null,
                RelativeFilePath = (string?)null,
                StartLine = (int?)null
            });
        var combined = codeNodes.Concat(projectNodes);

        var probe = await combined
            .OrderBy(static node => node.Name)
            .ThenBy(static node => node.Id)
            .Take(maxRelated + 1)
            .Select(static node => new Context360RelatedNode(
                node.Id,
                node.Name,
                node.CodeNodeId,
                node.RelativeFilePath,
                node.StartLine))
            .ToArrayAsync(ct);

        if (probe.Length <= maxRelated)
        {
            return new RelatedBucket(probe, probe.Length);
        }

        var total = await combined.CountAsync(ct);

        return new RelatedBucket(probe[..maxRelated], total);
    }

    private readonly record struct RelatedBucket(Context360RelatedNode[] Items, int Total)
    {
        public static RelatedBucket Empty { get; } = new([], 0);
    }
}
