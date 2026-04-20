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
                ProjectId = codeNode.ProjectId,
                FullyQualifiedName = codeNode.FullyQualifiedName,
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
                codeNode.ProjectId,
                codeNode.FullyQualifiedName,
                codeNode.NodeType,
                codeNode.RelativeFilePath,
                codeNode.StartLine,
                codeNode.EndLine,
                codeNode.Summary));

    public static async Task<CodeNode?> FindRootNodeAsync(
        SharpSenseDbContext context,
        string nodeIdentifier,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeIdentifier);

        var rootNode = await ProjectCodeNodes(
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => codeNode.Id == nodeIdentifier || codeNode.FullyQualifiedName == nodeIdentifier)
                    .OrderBy(static codeNode => codeNode.Id))
            .FirstOrDefaultAsync(ct);

        if (rootNode is not null)
        {
            return rootNode;
        }

        var normalizedIdentifier = nodeIdentifier.ToLowerInvariant();
        return await ProjectCodeNodes(
                context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => codeNode.FullyQualifiedName.ToLower() == normalizedIdentifier)
                    .OrderBy(static codeNode => codeNode.Id))
            .FirstOrDefaultAsync(ct);
    }
}
