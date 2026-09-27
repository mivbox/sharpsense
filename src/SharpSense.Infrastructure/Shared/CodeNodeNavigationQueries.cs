using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using System.Globalization;

namespace SharpSense.Infrastructure.Shared;

internal static class CodeNodeNavigationQueries
{
    public static IQueryable<CodeNode> ProjectCodeNodes(
        SharpSenseDbContext context,
        IQueryable<CodeNodeRecord> query)
        => from codeNode in query
           join graphNode in context.GraphNodes.AsNoTracking() on codeNode.Id equals graphNode.Id
           join document in context.Documents.AsNoTracking() on codeNode.DocumentId equals document.Id
           join projectGraphNode in context.GraphNodes.AsNoTracking() on codeNode.ProjectNodeId equals projectGraphNode.Id into projectGraphNodes
           from projectGraphNode in projectGraphNodes.DefaultIfEmpty()
           select new CodeNode
           {
               Id = codeNode.Id,
               CanonicalId = graphNode.CanonicalId,
               ProjectId = projectGraphNode == null ? null : projectGraphNode.CanonicalId,
               FullyQualifiedName = codeNode.FullyQualifiedName,
               DisplayName = codeNode.DisplayName,
               NodeType = codeNode.NodeType,
               RelativeFilePath = document.RelativePath,
               StartLine = codeNode.StartLine,
               EndLine = codeNode.EndLine,
               Summary = codeNode.Summary,
               SearchText = codeNode.SearchText,
               BodyHash = codeNode.BodyHash,
               VectorEmbedding = codeNode.VectorEmbedding
           };

    public static IQueryable<CodeNodeResult> ProjectCodeNodeResults(
        SharpSenseDbContext context,
        IQueryable<CodeNodeRecord> query)
        => from codeNode in query
           join graphNode in context.GraphNodes.AsNoTracking() on codeNode.Id equals graphNode.Id
           join document in context.Documents.AsNoTracking() on codeNode.DocumentId equals document.Id
           join projectGraphNode in context.GraphNodes.AsNoTracking() on codeNode.ProjectNodeId equals projectGraphNode.Id into projectGraphNodes
           from projectGraphNode in projectGraphNodes.DefaultIfEmpty()
           select new CodeNodeResult(
               codeNode.Id,
               graphNode.CanonicalId,
               projectGraphNode == null ? null : projectGraphNode.CanonicalId,
               codeNode.FullyQualifiedName,
               codeNode.DisplayName,
               codeNode.NodeType,
               document.RelativePath,
               codeNode.StartLine,
               codeNode.EndLine,
               codeNode.Summary);

    public static IQueryable<ProjectNode> ProjectProjectNodes(
        SharpSenseDbContext context,
        IQueryable<ProjectNodeRecord> query)
        => from projectNode in query
           join graphNode in context.GraphNodes.AsNoTracking() on projectNode.Id equals graphNode.Id
           join document in context.Documents.AsNoTracking() on projectNode.ProjectDocumentId equals document.Id
           select new ProjectNode
           {
               Id = graphNode.CanonicalId,
               Name = projectNode.Name,
               RelativeFilePath = document.RelativePath,
               ContentHash = projectNode.ContentHash
           };

    public static IQueryable<DependencyEdge> ProjectDependencyEdges(
        SharpSenseDbContext context,
        IQueryable<DependencyEdgeRecord> query)
        => from dependencyEdge in query
           join callerNode in context.GraphNodes.AsNoTracking() on dependencyEdge.CallerNodeId equals callerNode.Id
           join calleeNode in context.GraphNodes.AsNoTracking() on dependencyEdge.CalleeNodeId equals calleeNode.Id
           select new DependencyEdge
           {
               CallerId = callerNode.CanonicalId,
               CalleeId = calleeNode.CanonicalId,
               EdgeType = dependencyEdge.EdgeType,
               Metadata = dependencyEdge.Metadata
           };

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
                context,
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
            context,
            context.CodeNodes
                    .AsNoTracking()
                .OrderBy(static codeNode => codeNode.Id))
            .Where(codeNode => codeNode.CanonicalId == nodeIdentifier || codeNode.FullyQualifiedName == nodeIdentifier)
            .FirstOrDefaultAsync(ct);

        if (rootNode is not null)
        {
            return rootNode;
        }

        return await ProjectCodeNodes(
            context,
            context.CodeNodes
                .AsNoTracking()
                .OrderBy(static codeNode => codeNode.Id))
            .Where(codeNode => EF.Functions.Collate(codeNode.FullyQualifiedName, "NOCASE") == nodeIdentifier)
            .FirstOrDefaultAsync(ct);
    }
}
