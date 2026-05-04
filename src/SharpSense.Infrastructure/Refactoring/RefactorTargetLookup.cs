using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Infrastructure.Persistence;
using PersistedDocumentKind = SharpSense.Infrastructure.Persistence.Records.DocumentKind;

namespace SharpSense.Infrastructure.Refactoring;

public sealed class RefactorTargetLookup(IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : IRefactorTargetLookup
{
    public async Task<NodeRefactorTarget?> GetTarget(
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
                select new NodeRefactorTarget(
                    codeNode.Id,
                    document.RelativePath,
                    codeNode.StartLine,
                    codeNode.EndLine,
                    (
                        from projectNode in context.ProjectNodes.AsNoTracking()
                        join projectDocument in context.Documents.AsNoTracking() on projectNode.ProjectDocumentId equals projectDocument.Id
                        where projectNode.Id == codeNode.ProjectNodeId
                        select projectDocument.RelativePath
                    ).SingleOrDefault(),
                    document.Kind == PersistedDocumentKind.Source
                        ? DocumentKind.Source
                        : document.Kind == PersistedDocumentKind.ProjectFile
                            ? DocumentKind.ProjectFile
                            : document.Kind == PersistedDocumentKind.Markdown
                                ? DocumentKind.Markdown
                                : DocumentKind.Other))
            .SingleOrDefaultAsync(ct);
    }
}
