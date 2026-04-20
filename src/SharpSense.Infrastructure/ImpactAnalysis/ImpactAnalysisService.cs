using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Features.ImpactAnalysis.Contracts;
using SharpSense.Application.Features.ImpactAnalysis.ImpactAnalysis;
using SharpSense.Application.Features.ImpactAnalysis.Infrastructure;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Shared;

namespace SharpSense.Infrastructure.ImpactAnalysis;

public sealed class ImpactAnalysisService(IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : IImpactAnalysisService
{
    public async Task<ImpactAnalysisResult> Analyze(ImpactAnalysisQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Identifier);

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var rootNode = await CodeNodeNavigationQueries.FindRootNodeAsync(context, query.Identifier, ct);
        if (rootNode is null)
        {
            return new ImpactAnalysisResult(query.Identifier, [], []);
        }

        var includedEdgeTypes = query.IncludedEdgeTypes is { Length: > 0 }
            ? query.IncludedEdgeTypes
            : null;
        var maxTraversalDepth = query.IncludeTransitive
            ? Math.Max(query.MaxDepth, 1)
            : 1;
        var impactedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var impactedEdges = new HashSet<(string CallerId, string CalleeId, Domain.KnowledgeGraph.Enums.EdgeType EdgeType)>();
        var visitedNodeIds = new HashSet<string>(StringComparer.Ordinal) { rootNode.Id };
        var frontierNodeIds = new[] { rootNode.Id };

        for (var depth = 0; depth < maxTraversalDepth && frontierNodeIds.Length > 0; depth++)
        {
            var inboundEdgesQuery = context.DependencyEdges
                .AsNoTracking()
                .Where(edge => frontierNodeIds.Contains(edge.CalleeId));

            if (includedEdgeTypes is not null)
            {
                inboundEdgesQuery = inboundEdgesQuery.Where(edge => includedEdgeTypes.Contains(edge.EdgeType));
            }

            var inboundEdges = await inboundEdgesQuery
                .ToArrayAsync(ct)
                ;
            var nextFrontierNodeIds = new List<string>(inboundEdges.Length);

            foreach (var inboundEdge in inboundEdges)
            {
                impactedEdges.Add((inboundEdge.CallerId, inboundEdge.CalleeId, inboundEdge.EdgeType));

                if (impactedNodeIds.Add(inboundEdge.CallerId) && visitedNodeIds.Add(inboundEdge.CallerId))
                {
                    nextFrontierNodeIds.Add(inboundEdge.CallerId);
                }
            }

            frontierNodeIds = nextFrontierNodeIds
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        var orderedImpactedNodes = impactedNodeIds.Count == 0
            ? []
            : await context.CodeNodes
                .AsNoTracking()
                .Where(codeNode => impactedNodeIds.Contains(codeNode.Id))
                .OrderBy(codeNode => codeNode.FullyQualifiedName)
                .ThenBy(codeNode => codeNode.Id)
                .ToArrayAsync(ct)
                ;
        var orderedImpactedEdges = impactedEdges
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .Select(
                static edge => new DependencyEdge
                {
                    CallerId = edge.CallerId,
                    CalleeId = edge.CalleeId,
                    EdgeType = edge.EdgeType
                })
            .ToArray();

        return new ImpactAnalysisResult(
            rootNode.FullyQualifiedName,
            ImpactAnalysisMapper.ToImpactedCodeNodes(orderedImpactedNodes),
            ImpactAnalysisMapper.ToImpactedDependencyEdges(orderedImpactedEdges));
    }
}
