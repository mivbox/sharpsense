using Microsoft.EntityFrameworkCore;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Shared;

namespace SharpSense.Infrastructure.Trace;

internal sealed class TraceNavigator(IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : ITraceNavigator
{
    public async Task<CodeNodeResult?> GetRootNode(string identifier, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var rootNode = await CodeNodeNavigationQueries.FindRootNode(context, identifier, ct);

        return rootNode is null
            ? null
            : new CodeNodeResult(
                rootNode.Id,
                rootNode.CanonicalId,
                rootNode.ProjectId,
                rootNode.FullyQualifiedName,
                rootNode.DisplayName,
                rootNode.NodeType,
                rootNode.RelativeFilePath,
                rootNode.StartLine,
                rootNode.EndLine,
                rootNode.Summary);
    }

    public async Task<CodeNodeResult[]> GetCallees(TraceQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Identifier);

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var rootNode = await CodeNodeNavigationQueries.FindRootNode(context, query.Identifier, ct);
        if (rootNode is null)
        {
            return [];
        }

        var includedEdgeTypes = query.IncludedEdgeTypes is { Length: > 0 }
            ? query.IncludedEdgeTypes
            : KnowledgeGraphEdgeTypes.Functional;
        var calleeIds = await context.DependencyEdges
            .AsNoTracking()
            .Where(edge =>
                edge.CallerNodeId == rootNode.Id &&
                includedEdgeTypes.Contains(edge.EdgeType))
            .Select(static edge => edge.CalleeNodeId)
            .Distinct()
            .ToArrayAsync(ct);
        if (calleeIds.Length == 0)
        {
            return [];
        }

        return await CodeNodeNavigationQueries.ProjectCodeNodeResults(
            context,
            context.CodeNodes
                .AsNoTracking()
                .Where(codeNode => calleeIds.Contains(codeNode.Id))
                .OrderBy(static codeNode => codeNode.FullyQualifiedName)
                .ThenBy(static codeNode => codeNode.Id))
            .ToArrayAsync(ct);
    }

    public async Task<ImpactedDependencyEdge[]> GetDependencies(
        IReadOnlyCollection<CodeNodeResult> nodes,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var byId = nodes.ToDictionary(node => node.Id);
        var ids = byId.Keys.ToArray();
        var edgeTypes = KnowledgeGraphEdgeTypes.Functional;
        var edges = await db.DependencyEdges
            .AsNoTracking()
            .Where(edge =>
                EF.Parameter(ids)
                    .Contains(edge.CallerNodeId) &&
                EF.Parameter(ids)
                    .Contains(edge.CalleeNodeId) &&
                edgeTypes.Contains(edge.EdgeType))
            .Select(edge => new
            {
                edge.CallerNodeId,
                edge.CalleeNodeId,
                edge.EdgeType
            })
            .ToArrayAsync(ct);

        return edges
            .Select(edge => new ImpactedDependencyEdge(
                byId[edge.CallerNodeId].CanonicalId,
                byId[edge.CalleeNodeId].CanonicalId,
                edge.EdgeType))
            .ToArray();
    }
}
