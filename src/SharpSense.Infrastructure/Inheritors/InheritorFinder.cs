using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Inheritors.Abstractions;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Shared;

namespace SharpSense.Infrastructure.Inheritors;

internal sealed class InheritorFinder(IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : IInheritorFinder
{
    public async Task<CodeNodeResult[]> GetInheritors(
        GetInheritorsQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.NodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query.NodeId),
                query.NodeId,
                "NodeId must be greater than zero.");
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var derivedNodeIds = await context.DependencyEdges
            .AsNoTracking()
            .Where(edge =>
                edge.CalleeNodeId == query.NodeId &&
                edge.EdgeType == EdgeType.Implements)
            .Select(static edge => edge.CallerNodeId)
            .Distinct()
            .ToArrayAsync(ct);
        if (derivedNodeIds.Length == 0)
        {
            return [];
        }

        return await CodeNodeNavigationQueries.ProjectCodeNodeResults(
            context,
            context.CodeNodes
                .AsNoTracking()
                .Where(codeNode =>
                    EF.Parameter(derivedNodeIds)
                        .Contains(codeNode.Id) &&
                    codeNode.NodeType == NodeType.Class)
                .OrderBy(static codeNode => codeNode.FullyQualifiedName)
                .ThenBy(static codeNode => codeNode.Id))
            .ToArrayAsync(ct);
    }
}
