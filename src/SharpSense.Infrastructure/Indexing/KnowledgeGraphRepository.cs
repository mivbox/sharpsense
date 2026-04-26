using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.Indexing;

public sealed class KnowledgeGraphRepository(
    IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : IKnowledgeGraphRepository
{
    public async Task ReplaceTarget(
        ExtractedNodes extractedNodes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extractedNodes);

        var projectNodes = extractedNodes.Projects
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ThenBy(static project => project.Id, StringComparer.Ordinal)
            .Select(ToProjectNode)
            .ToArray();
        var codeNodes = extractedNodes.CodeNodes
            .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .Select(ToCodeNode)
            .ToArray();
        var dependencyEdges = extractedNodes.Edges
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .Select(ToDependencyEdge)
            .ToArray();
        var documentNodeCount = codeNodes.Count(static codeNode => codeNode.NodeType == NodeType.Document);

        using var trace = SharpSenseTraceSpan.Start("index.persist");
        trace.AddTag("index.project.count", projectNodes.Length);
        trace.AddTag("index.code_node.count", codeNodes.Length);
        trace.AddTag("index.document_node.count", documentNodeCount);
        trace.AddTag("index.dependency.count", dependencyEdges.Length);
        trace.AddTag("index.diagnostic.count", extractedNodes.Diagnostics.Count);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            await AssignPersistedCodeNodeIds(context, codeNodes, ct);

            await context.DependencyEdges.ExecuteDeleteAsync(ct);
            await context.CodeNodes.ExecuteDeleteAsync(ct);
            await context.ProjectNodes.ExecuteDeleteAsync(ct);
            context.ChangeTracker.Clear();

            await context.ProjectNodes.AddRangeAsync(projectNodes, ct);
            await context.CodeNodes.AddRangeAsync(codeNodes, ct);
            await context.DependencyEdges.AddRangeAsync(dependencyEdges, ct);
            await context.SaveChangesAsync(ct);

            await RefreshSearchIndex(context, ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception)
        {
            trace.RecordExceptionAndErrorStatus(exception);
            throw;
        }
    }

    public async Task ReplaceWorkspaceFiles(
        IReadOnlyList<string> relativeFilePaths,
        ExtractedNodes extractedNodes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(relativeFilePaths);
        ArgumentNullException.ThrowIfNull(extractedNodes);

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var changedFilePaths = relativeFilePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(pathComparer)
            .OrderBy(static path => path, pathComparer)
            .ToArray();
        if (changedFilePaths.Length == 0)
        {
            return;
        }

        var codeNodes = extractedNodes.CodeNodes
            .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .Select(ToCodeNode)
            .ToArray();
        var dependencyEdges = extractedNodes.Edges
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .Select(ToDependencyEdge)
            .ToArray();

        using var trace = SharpSenseTraceSpan.Start("index.persist.incremental");
        trace.AddTag("index.code_node.count", codeNodes.Length);
        trace.AddTag("index.dependency.count", dependencyEdges.Length);
        trace.AddTag("index.diagnostic.count", extractedNodes.Diagnostics.Count);

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            var changedNodeIdentities = await context.CodeNodes
                .AsNoTracking()
                .Where(codeNode => changedFilePaths.Contains(codeNode.RelativeFilePath))
                .Select(static codeNode => new PersistedCodeNodeIdentity(codeNode.Id, codeNode.CanonicalId))
                .ToArrayAsync(ct);
            AssignPersistedCodeNodeIds(codeNodes, changedNodeIdentities);

            var changedCanonicalIds = changedNodeIdentities
                .Select(static codeNode => codeNode.CanonicalId)
                .ToArray();
            var persistedCanonicalIds = codeNodes
                .Select(static codeNode => codeNode.CanonicalId)
                .ToHashSet(StringComparer.Ordinal);
            var removedCanonicalIds = changedCanonicalIds
                .Where(nodeId => !persistedCanonicalIds.Contains(nodeId))
                .ToArray();

            if (changedCanonicalIds.Length > 0)
            {
                if (removedCanonicalIds.Length > 0)
                {
                    await context.DependencyEdges
                        .Where(edge =>
                            changedCanonicalIds.Contains(edge.CallerId) ||
                            removedCanonicalIds.Contains(edge.CalleeId))
                        .ExecuteDeleteAsync(ct);
                }
                else
                {
                    await context.DependencyEdges
                        .Where(edge => changedCanonicalIds.Contains(edge.CallerId))
                        .ExecuteDeleteAsync(ct);
                }
            }

            foreach (var changedFilePath in changedFilePaths)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM CodeNodeSearch WHERE RelativeFilePath = {changedFilePath}",
                    ct);
            }

            await context.CodeNodes
                .Where(codeNode => changedFilePaths.Contains(codeNode.RelativeFilePath))
                .ExecuteDeleteAsync(ct);

            context.ChangeTracker.Clear();

            if (codeNodes.Length > 0)
            {
                await context.CodeNodes.AddRangeAsync(codeNodes, ct);
            }

            if (dependencyEdges.Length > 0)
            {
                await context.DependencyEdges.AddRangeAsync(dependencyEdges, ct);
            }

            await context.SaveChangesAsync(ct);

            foreach (var codeNode in codeNodes)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT OR REPLACE INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath)
                     VALUES ({codeNode.Id}, {codeNode.CanonicalId}, {codeNode.DisplayName}, {codeNode.FullyQualifiedName}, {codeNode.Summary}, {codeNode.RelativeFilePath})
                     """,
                    ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch (Exception exception)
        {
            trace.RecordExceptionAndErrorStatus(exception);
            throw;
        }
    }

    private static ProjectNode ToProjectNode(IndexedProject project)
    {
        return new ProjectNode
        {
            Id = project.Id,
            Name = project.Name,
            RelativeFilePath = project.RelativeFilePath,
            ContentHash = project.ContentHash
        };
    }

    private static CodeNode ToCodeNode(IndexedCodeNode codeNode)
    {
        return new CodeNode
        {
            CanonicalId = codeNode.CanonicalId,
            ProjectId = codeNode.ProjectId,
            FullyQualifiedName = codeNode.FullyQualifiedName,
            DisplayName = codeNode.DisplayName,
            NodeType = codeNode.NodeType,
            RelativeFilePath = codeNode.RelativeFilePath,
            StartLine = codeNode.StartLine,
            EndLine = codeNode.EndLine,
            Summary = codeNode.Summary,
            VectorEmbedding = codeNode.VectorEmbedding
        };
    }

    private static DependencyEdge ToDependencyEdge(IndexedDependency dependency)
    {
        return new DependencyEdge
        {
            CallerId = dependency.CallerId,
            CalleeId = dependency.CalleeId,
            EdgeType = dependency.EdgeType
        };
    }

    private static async Task RefreshSearchIndex(
        SharpSenseDbContext context,
        CancellationToken ct)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM CodeNodeSearch;
            INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath)
            SELECT Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath
            FROM CodeNodes;
            """,
            ct);
    }

    private static async Task AssignPersistedCodeNodeIds(
        SharpSenseDbContext context,
        IReadOnlyCollection<CodeNode> codeNodes,
        CancellationToken ct)
    {
        var persistedIdsByCanonicalId = await context.CodeNodes
            .AsNoTracking()
            .Select(static codeNode => new PersistedCodeNodeIdentity(codeNode.Id, codeNode.CanonicalId))
            .ToDictionaryAsync(
                static codeNode => codeNode.CanonicalId,
                static codeNode => codeNode.Id,
                StringComparer.Ordinal,
                ct);

        AssignPersistedCodeNodeIds(codeNodes, persistedIdsByCanonicalId);
    }

    private static void AssignPersistedCodeNodeIds(
        IEnumerable<CodeNode> codeNodes,
        IEnumerable<PersistedCodeNodeIdentity> persistedCodeNodes)
    {
        var persistedIdsByCanonicalId = persistedCodeNodes
            .ToDictionary(
                static codeNode => codeNode.CanonicalId,
                static codeNode => codeNode.Id,
                StringComparer.Ordinal);

        AssignPersistedCodeNodeIds(codeNodes, persistedIdsByCanonicalId);
    }

    private static void AssignPersistedCodeNodeIds(
        IEnumerable<CodeNode> codeNodes,
        IReadOnlyDictionary<string, int> persistedIdsByCanonicalId)
    {
        foreach (var codeNode in codeNodes)
        {
            if (persistedIdsByCanonicalId.TryGetValue(codeNode.CanonicalId, out var persistedId))
            {
                codeNode.Id = persistedId;
            }
        }
    }

    private sealed record PersistedCodeNodeIdentity(int Id, string CanonicalId);
}
