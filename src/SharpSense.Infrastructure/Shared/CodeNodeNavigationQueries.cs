using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.Shared;

public static class CodeNodeNavigationQueries
{
    public static IQueryable<CodeNode> ProjectCodeNodes(IQueryable<CodeNode> query)
        => query.Select(
            static codeNode => new CodeNode
            {
                Id = codeNode.Id,
                CanonicalId = codeNode.CanonicalId,
                ProjectId = codeNode.ProjectId,
                FullyQualifiedName = codeNode.FullyQualifiedName,
                DisplayName = codeNode.DisplayName,
                NodeType = codeNode.NodeType,
                RelativeFilePath = codeNode.RelativeFilePath,
                StartLine = codeNode.StartLine,
                EndLine = codeNode.EndLine,
                Summary = codeNode.Summary
            });

    public static IQueryable<CodeNodeResult> ProjectCodeNodeResults(IQueryable<CodeNode> query)
        => query.Select(
            static codeNode => new CodeNodeResult(
                codeNode.Id,
                codeNode.CanonicalId,
                codeNode.ProjectId,
                codeNode.FullyQualifiedName,
                codeNode.DisplayName,
                codeNode.NodeType,
                codeNode.RelativeFilePath,
                codeNode.StartLine,
                codeNode.EndLine,
                codeNode.Summary));

    public static async Task<CodeNode?> FindRootNode(
        SharpSenseDbContext context,
        string nodeIdentifier,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeIdentifier);

        if (int.TryParse(nodeIdentifier, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId))
        {
            var rootNodeById = await ProjectCodeNodes(
                    context.CodeNodes
                        .AsNoTracking()
                        .Where(codeNode => codeNode.Id == parsedId))
                .FirstOrDefaultAsync(ct);

            if (rootNodeById is not null)
            {
                return rootNodeById;
            }
        }

        var rootNode = await ProjectCodeNodes(
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => codeNode.CanonicalId == nodeIdentifier || codeNode.FullyQualifiedName == nodeIdentifier)
                    .OrderBy(static codeNode => codeNode.Id))
            .FirstOrDefaultAsync(ct);

        if (rootNode is not null)
        {
            return rootNode;
        }

        return await ProjectCodeNodes(
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => EF.Functions.Collate(codeNode.FullyQualifiedName, "NOCASE") == nodeIdentifier)
                    .OrderBy(static codeNode => codeNode.Id))
            .FirstOrDefaultAsync(ct);
    }
}
