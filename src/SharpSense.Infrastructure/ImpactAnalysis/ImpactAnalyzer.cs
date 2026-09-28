using Microsoft.EntityFrameworkCore;
using SharpSense.Application.ImpactAnalysis.Abstractions;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Shared;

namespace SharpSense.Infrastructure.ImpactAnalysis;

internal sealed class ImpactAnalyzer(IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : IImpactAnalyzer
{
    public async Task<ImpactAnalysisResult> Analyze(ImpactAnalysisQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Identifier);

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var rootNode = await CodeNodeNavigationQueries.FindRootNode(context, query.Identifier, ct);
        if (rootNode is null)
        {
            return new ImpactAnalysisResult(query.Identifier, [], []);
        }

        var includedEdgeTypes = query.IncludedEdgeTypes is { Length: > 0 }
            ? query.IncludedEdgeTypes
            : KnowledgeGraphEdgeTypes.Functional;
        var maxTraversalDepth = query.IncludeTransitive
            ? Math.Max(query.MaxDepth, 1)
            : 1;
        var impactedNodeIds = new HashSet<int>();
        var impactedEdges = new HashSet<(int CallerNodeId, int CalleeNodeId, Domain.KnowledgeGraph.Enums.EdgeType EdgeType)>();
        var visitedNodeIds = new HashSet<int>
        {
            rootNode.Id
        };
        var frontierNodeIds = new[]
        {
            rootNode.Id
        };

        for (var depth = 0; depth < maxTraversalDepth && frontierNodeIds.Length > 0; depth++)
        {
            var inboundEdgesQuery = context.DependencyEdges
                .AsNoTracking()
                .Where(edge => frontierNodeIds.Contains(edge.CalleeNodeId));

            inboundEdgesQuery = inboundEdgesQuery.Where(edge => includedEdgeTypes.Contains(edge.EdgeType));

            var inboundEdges = await inboundEdgesQuery
                .ToArrayAsync(ct)
                ;
            var nextFrontierNodeIds = new List<int>(inboundEdges.Length);

            foreach (var inboundEdge in inboundEdges)
            {
                impactedEdges.Add((inboundEdge.CallerNodeId, inboundEdge.CalleeNodeId, inboundEdge.EdgeType));

                if (impactedNodeIds.Add(inboundEdge.CallerNodeId) && visitedNodeIds.Add(inboundEdge.CallerNodeId))
                {
                    nextFrontierNodeIds.Add(inboundEdge.CallerNodeId);
                }
            }

            frontierNodeIds = nextFrontierNodeIds
                .Distinct()
                .ToArray();
        }

        var orderedImpactedNodes = impactedNodeIds.Count == 0
            ? []
            : await CodeNodeNavigationQueries.ProjectCodeNodes(
                context,
                context.CodeNodes
                        .AsNoTracking()
                    .Where(codeNode => impactedNodeIds.Contains(codeNode.Id))
                    .OrderBy(codeNode => codeNode.FullyQualifiedName)
                    .ThenBy(codeNode => codeNode.Id))
                .ToArrayAsync(ct)
                ;
        var edgeNodeIds = impactedEdges
            .SelectMany(static edge => new[]
            {
                edge.CallerNodeId,
                edge.CalleeNodeId
            })
            .Distinct()
            .ToArray();
        var canonicalIdsByNodeId = edgeNodeIds.Length == 0
            ? []
            : await context.GraphNodes
                .AsNoTracking()
                .Where(graphNode => edgeNodeIds.Contains(graphNode.Id))
                .ToDictionaryAsync(static graphNode => graphNode.Id, static graphNode => graphNode.CanonicalId, ct);
        var orderedImpactedEdges = impactedEdges
            .OrderBy(edge => canonicalIdsByNodeId[edge.CallerNodeId], StringComparer.Ordinal)
            .ThenBy(edge => canonicalIdsByNodeId[edge.CalleeNodeId], StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .Select(
                edge => new DependencyEdge
                {
                    CallerId = canonicalIdsByNodeId[edge.CallerNodeId],
                    CalleeId = canonicalIdsByNodeId[edge.CalleeNodeId],
                    EdgeType = edge.EdgeType
                })
            .ToArray();

        return new ImpactAnalysisResult(
            rootNode.FullyQualifiedName,
            ImpactAnalysisMapper.ToImpactedCodeNodes(orderedImpactedNodes),
            ImpactAnalysisMapper.ToImpactedDependencyEdges(orderedImpactedEdges));
    }
}
