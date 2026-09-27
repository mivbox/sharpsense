using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Shared;
using static SharpSense.Infrastructure.Indexing.GraphPaths;
using static SharpSense.Infrastructure.Indexing.PersistedGraphBuilder;

namespace SharpSense.Infrastructure.Indexing;

internal static class GraphPersistence
{
    internal static Task AdvanceGraphRevision(SharpSenseDbContext context, CancellationToken ct)
    {
        // Page cursors must observe the same commit as graph rows, not the later run summary.
        var revision = Guid.NewGuid()
            .ToString("N");

        return context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO IndexRunState (Id, GraphRevision) VALUES (1, {revision})
            ON CONFLICT (Id) DO UPDATE SET GraphRevision = excluded.GraphRevision;
            """,
            ct);
    }

    internal static async Task<ExtractedNodes> LoadCurrentSnapshot(
        SharpSenseDbContext context,
        CancellationToken ct)
    {
        var projects = await CodeNodeNavigationQueries.ProjectProjectNodes(
            context,
            context.ProjectNodes
                .AsNoTracking()
                .OrderBy(static projectNode => projectNode.Name)
                .ThenBy(static projectNode => projectNode.Id))
            .Select(
                static projectNode => new IndexedProject(
                    projectNode.Id,
                    projectNode.Name,
                    projectNode.RelativeFilePath,
                    projectNode.ContentHash))
            .ToArrayAsync(ct);
        var codeNodes = await CodeNodeNavigationQueries.ProjectCodeNodes(
            context,
            context.CodeNodes
                .AsNoTracking()
                .OrderBy(static codeNode => codeNode.FullyQualifiedName)
                .ThenBy(static codeNode => codeNode.Id))
            .Select(
                static codeNode => new IndexedCodeNode(
                    codeNode.CanonicalId,
                    codeNode.ProjectId,
                    codeNode.FullyQualifiedName,
                    codeNode.DisplayName,
                    codeNode.NodeType,
                    codeNode.RelativeFilePath,
                    codeNode.StartLine,
                    codeNode.EndLine,
                    codeNode.Summary,
                    codeNode.SearchText,
                    codeNode.BodyHash,
                    codeNode.VectorEmbedding))
            .ToArrayAsync(ct);
        var edges = (await CodeNodeNavigationQueries.ProjectDependencyEdges(
            context,
            context.DependencyEdges
                .AsNoTracking()
                .OrderBy(static edge => edge.CallerNodeId)
                .ThenBy(static edge => edge.CalleeNodeId)
                .ThenBy(static edge => edge.EdgeType))
            .Select(
                static edge => new IndexedDependency(
                    edge.CallerId,
                    edge.CalleeId,
                    edge.EdgeType,
                    edge.Metadata))
            .ToArrayAsync(ct))
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .ToArray();

        return new ExtractedNodes(projects, codeNodes, edges, []);
    }

    internal static async Task<IReadOnlyList<IndexedCodeNode>> LoadPersistedCodeNodes(
        SharpSenseDbContext context,
        CancellationToken ct)
    {
        IQueryable<CodeNodeRecord> codeNodeQuery = context.CodeNodes
            .AsNoTracking()
            .OrderBy(static codeNode => codeNode.FullyQualifiedName)
            .ThenBy(static codeNode => codeNode.Id);

        return await CodeNodeNavigationQueries.ProjectCodeNodes(context, codeNodeQuery)
            .Select(
                static codeNode => new IndexedCodeNode(
                    codeNode.CanonicalId,
                    codeNode.ProjectId,
                    codeNode.FullyQualifiedName,
                    codeNode.DisplayName,
                    codeNode.NodeType,
                    codeNode.RelativeFilePath,
                    codeNode.StartLine,
                    codeNode.EndLine,
                    codeNode.Summary,
                    codeNode.SearchText,
                    codeNode.BodyHash,
                    codeNode.VectorEmbedding))
            .ToArrayAsync(ct);
    }

    internal static async Task<PersistedIdentityMaps> LoadIdentityMaps(
        SharpSenseDbContext context,
        CancellationToken ct)
    {
        var pathComparer = GetPathComparer();
        var persistedDirectories = await context.Directories
            .AsNoTracking()
            .Select(static directory => new
            {
                directory.Path,
                directory.Id
            })
            .ToArrayAsync(ct);
        var persistedDocuments = await context.Documents
            .AsNoTracking()
            .Select(static document => new
            {
                document.RelativePath,
                document.Id
            })
            .ToArrayAsync(ct);
        var persistedGraphNodes = await context.GraphNodes
            .AsNoTracking()
            .Select(static graphNode => new
            {
                graphNode.CanonicalId,
                graphNode.Id
            })
            .ToArrayAsync(ct);

        return new PersistedIdentityMaps(
            persistedDirectories.ToDictionary(static directory => directory.Path, static directory => directory.Id, pathComparer),
            persistedDocuments.ToDictionary(static document => document.RelativePath, static document => document.Id, pathComparer),
            persistedGraphNodes.ToDictionary(static graphNode => graphNode.CanonicalId, static graphNode => graphNode.Id, StringComparer.Ordinal));
    }

    internal static async Task ReplacePersistedGraph(
        SharpSenseDbContext context,
        PersistedGraph graph,
        PersistedIdentityMaps identityMaps,
        CancellationToken ct)
    {
        // Preserve every surviving FK target, including directories and documents: deleting any
        // ancestor would cascade through CodeNodes and destroy their authored memories.
        await context.DependencyEdges.ExecuteDeleteAsync(ct);
        await context.DirectoryClosures.ExecuteDeleteAsync(ct);
        var graphNodeIds = graph.GraphNodes.Select(static node => node.Id)
            .ToArray();
        await context.GraphNodes.Where(node => !EF.Parameter(graphNodeIds)
            .Contains(node.Id))
            .ExecuteDeleteAsync(ct);
        var documentIds = graph.Documents.Select(static document => document.Id)
            .ToArray();
        // Documents and directories are pruned after surviving nodes have been moved to new files.
        var directoryIds = graph.Directories.Select(static directory => directory.Id)
            .ToArray();
        var persistedCodeNodeIds = await context.CodeNodes.Select(static node => node.Id)
            .ToHashSetAsync(ct);
        var persistedProjectNodeIds = await context.ProjectNodes.Select(static node => node.Id)
            .ToHashSetAsync(ct);
        context.ChangeTracker.Clear();

        UpsertDirectories(context, graph.Directories, identityMaps);
        if (graph.DirectoryClosures.Length > 0)
        {
            await context.DirectoryClosures.AddRangeAsync(graph.DirectoryClosures, ct);
        }
        UpsertDocuments(context, graph.Documents, identityMaps);
        UpsertGraphNodes(context, graph.GraphNodes, identityMaps);
        UpsertCodeNodes(context, graph.CodeNodes, persistedCodeNodeIds);
        UpsertProjectNodes(context, graph.ProjectNodes, persistedProjectNodeIds);

        if (graph.DependencyEdges.Length > 0)
        {
            await context.DependencyEdges.AddRangeAsync(graph.DependencyEdges, ct);
        }

        await context.SaveChangesAsync(ct);
        await context.Documents.Where(document => !EF.Parameter(documentIds)
            .Contains(document.Id))
            .ExecuteDeleteAsync(ct);
        await context.Directories.Where(directory => !EF.Parameter(directoryIds)
            .Contains(directory.Id))
            .ExecuteDeleteAsync(ct);
        await RefreshSearchIndex(context, ct);
    }

    private static void UpsertDirectories(SharpSenseDbContext context, DirectoryRecord[] directories, PersistedIdentityMaps identityMaps)
    {
        if (directories.Length == 0)
        {
            return;
        }

        foreach (var directory in directories)
        {
            if (identityMaps.DirectoryIdsByPath.ContainsKey(directory.Path))
            {
                context.Directories.Update(directory);
            }
            else
            {
                context.Directories.Add(directory);
            }
        }
    }

    private static void UpsertDocuments(SharpSenseDbContext context, DocumentRecord[] documents, PersistedIdentityMaps identityMaps)
    {
        if (documents.Length == 0)
        {
            return;
        }

        foreach (var document in documents)
        {
            if (identityMaps.DocumentIdsByRelativePath.ContainsKey(document.RelativePath))
            {
                context.Documents.Update(document);
            }
            else
            {
                context.Documents.Add(document);
            }
        }
    }

    private static void UpsertGraphNodes(SharpSenseDbContext context, GraphNodeRecord[] graphNodes, PersistedIdentityMaps identityMaps)
    {
        if (graphNodes.Length == 0)
        {
            return;
        }

        foreach (var graphNode in graphNodes)
        {
            if (identityMaps.GraphNodeIdsByCanonicalId.ContainsKey(graphNode.CanonicalId))
            {
                context.GraphNodes.Update(graphNode);
            }
            else
            {
                context.GraphNodes.Add(graphNode);
            }
        }
    }

    private static void UpsertCodeNodes(SharpSenseDbContext context, CodeNodeRecord[] codeNodes, IReadOnlySet<int> persistedIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(codeNodes);

        if (codeNodes.Length == 0)
        {
            return;
        }

        foreach (var codeNode in codeNodes)
        {
            if (persistedIds.Contains(codeNode.Id))
            {
                context.CodeNodes.Update(codeNode);
            }
            else
            {
                context.CodeNodes.Add(codeNode);
            }
        }
    }

    private static void UpsertProjectNodes(SharpSenseDbContext context, ProjectNodeRecord[] projectNodes, IReadOnlySet<int> persistedIds)
    {
        if (projectNodes.Length == 0)
        {
            return;
        }

        foreach (var projectNode in projectNodes)
        {
            if (persistedIds.Contains(projectNode.Id))
            {
                context.ProjectNodes.Update(projectNode);
            }
            else
            {
                context.ProjectNodes.Add(projectNode);
            }
        }
    }

    private static async Task RefreshSearchIndex(
        SharpSenseDbContext context,
        CancellationToken ct)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM CodeNodeSearch;
            INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, SearchText, RelativeFilePath)
            SELECT CodeNodes.Id, GraphNodes.CanonicalId, CodeNodes.DisplayName, CodeNodes.FullyQualifiedName, CodeNodes.SearchText, Documents.RelativePath
            FROM CodeNodes
            INNER JOIN GraphNodes ON GraphNodes.Id = CodeNodes.Id
            INNER JOIN Documents ON Documents.Id = CodeNodes.DocumentId;
            """,
            ct);
    }
}
