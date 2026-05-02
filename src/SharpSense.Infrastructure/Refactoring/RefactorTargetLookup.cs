using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.Refactoring;

public sealed class RefactorTargetLookup(IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : IRefactorTargetLookup
{
    public async Task<RefactorTarget?> GetTarget(
        int nodeId,
        CancellationToken ct)
    {
        if (nodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId), nodeId, "Node id must be greater than zero.");
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);

        return await (
                from codeNode in context.CodeNodes.AsNoTracking()
                join document in context.Documents.AsNoTracking() on codeNode.DocumentId equals document.Id
                where codeNode.Id == nodeId
                select new RefactorTarget(
                    codeNode.Id,
                    document.RelativePath,
                    codeNode.StartLine,
                    codeNode.EndLine))
            .SingleOrDefaultAsync(ct);
    }
}
