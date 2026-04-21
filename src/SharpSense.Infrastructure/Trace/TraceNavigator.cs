using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Features.Trace;
using SharpSense.Application.Features.Trace.Infrastructure;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Shared;

namespace SharpSense.Infrastructure.Trace;

public sealed class TraceNavigator(IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : ITraceNavigator
{
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

        var calleeIds = await context.DependencyEdges
            .AsNoTracking()
            .Where(edge => edge.CallerId == rootNode.Id)
            .Select(static edge => edge.CalleeId)
            .Distinct()
            .ToArrayAsync(ct);
        if (calleeIds.Length == 0)
        {
            return [];
        }

        return await CodeNodeNavigationQueries.ProjectCodeNodeResults(
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => calleeIds.Contains(codeNode.Id))
                    .OrderBy(static codeNode => codeNode.FullyQualifiedName)
                    .ThenBy(static codeNode => codeNode.Id))
            .ToArrayAsync(ct);
    }
}
