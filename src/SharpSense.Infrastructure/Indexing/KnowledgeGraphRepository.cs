using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Shared;

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

        using var trace = SharpSenseTraceSpan.Start("index.persist");

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var identityMaps = await LoadIdentityMaps(context, ct);
            var persistedGraph = BuildPersistedGraph(extractedNodes, identityMaps);

            AddTraceCounts(trace, persistedGraph, extractedNodes.Diagnostics.Count);
            await ReplacePersistedGraph(context, persistedGraph, ct);
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

        var changedFilePaths = relativeFilePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeRelativePath)
            .Distinct(GetPathComparer())
            .OrderBy(static path => path, GetPathComparer())
            .ToArray();
        if (changedFilePaths.Length == 0)
        {
            return;
        }

        using var trace = SharpSenseTraceSpan.Start("index.persist.incremental");

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var currentSnapshot = await LoadCurrentSnapshot(context, ct);
            var mergedSnapshot = MergeSnapshots(currentSnapshot, changedFilePaths, extractedNodes);
            var identityMaps = await LoadIdentityMaps(context, ct);
            var persistedGraph = BuildPersistedGraph(mergedSnapshot, identityMaps);

            AddTraceCounts(trace, persistedGraph, extractedNodes.Diagnostics.Count);
            await ReplacePersistedGraph(context, persistedGraph, ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception)
        {
            trace.RecordExceptionAndErrorStatus(exception);
            throw;
        }
    }

    private static async Task<ExtractedNodes> LoadCurrentSnapshot(
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
                    codeNode.VectorEmbedding))
            .ToArrayAsync(ct);
        var edges = await CodeNodeNavigationQueries.ProjectDependencyEdges(
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
                    edge.EdgeType))
            .ToArrayAsync(ct);

        return new ExtractedNodes(projects, codeNodes, edges, []);
    }

    private static ExtractedNodes MergeSnapshots(
        ExtractedNodes currentSnapshot,
        IReadOnlyCollection<string> changedFilePaths,
        ExtractedNodes updatedSnapshot)
    {
        var pathComparer = GetPathComparer();
        var changedPaths = changedFilePaths.ToHashSet(pathComparer);
        var currentChangedProjectIds = currentSnapshot.Projects
            .Where(project => changedPaths.Contains(NormalizeRelativePath(project.RelativeFilePath)))
            .Select(static project => project.Id)
            .ToArray();
        var currentChangedCodeNodeIds = currentSnapshot.CodeNodes
            .Where(codeNode => changedPaths.Contains(NormalizeRelativePath(codeNode.RelativeFilePath)))
            .Select(static codeNode => codeNode.CanonicalId)
            .ToArray();
        var changedCallerIds = currentChangedProjectIds
            .Concat(currentChangedCodeNodeIds)
            .ToHashSet(StringComparer.Ordinal);
        var updatedNodeIds = updatedSnapshot.Projects
            .Select(static project => project.Id)
            .Concat(updatedSnapshot.CodeNodes.Select(static codeNode => codeNode.CanonicalId))
            .ToHashSet(StringComparer.Ordinal);
        var removedNodeIds = currentChangedProjectIds
            .Concat(currentChangedCodeNodeIds)
            .Where(nodeId => !updatedNodeIds.Contains(nodeId))
            .ToHashSet(StringComparer.Ordinal);

        var mergedProjects = currentSnapshot.Projects
            .Where(project => !changedPaths.Contains(NormalizeRelativePath(project.RelativeFilePath)))
            .Concat(updatedSnapshot.Projects)
            .GroupBy(static project => project.Id, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ThenBy(static project => project.Id, StringComparer.Ordinal)
            .ToArray();
        var mergedCodeNodes = currentSnapshot.CodeNodes
            .Where(codeNode => !changedPaths.Contains(NormalizeRelativePath(codeNode.RelativeFilePath)))
            .Concat(updatedSnapshot.CodeNodes)
            .GroupBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .ToArray();
        var mergedEdges = currentSnapshot.Edges
            .Where(edge =>
                !changedCallerIds.Contains(edge.CallerId) &&
                !removedNodeIds.Contains(edge.CallerId) &&
                !removedNodeIds.Contains(edge.CalleeId))
            .Concat(updatedSnapshot.Edges)
            .GroupBy(static edge => (edge.CallerId, edge.CalleeId, edge.EdgeType))
            .Select(static group => group.Last())
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .ToArray();

        return new ExtractedNodes(
            mergedProjects,
            mergedCodeNodes,
            mergedEdges,
            updatedSnapshot.Diagnostics);
    }

    private static PersistedGraph BuildPersistedGraph(
        ExtractedNodes extractedNodes,
        PersistedIdentityMaps identityMaps)
    {
        var normalizedProjects = extractedNodes.Projects
            .Select(
                static project => project with
                {
                    RelativeFilePath = NormalizeRelativePath(project.RelativeFilePath)
                })
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ThenBy(static project => project.Id, StringComparer.Ordinal)
            .ToArray();
        var normalizedCodeNodes = extractedNodes.CodeNodes
            .Select(
                static codeNode => codeNode with
                {
                    RelativeFilePath = NormalizeRelativePath(codeNode.RelativeFilePath)
                })
            .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .ToArray();
        var normalizedEdges = extractedNodes.Edges
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .ToArray();
        var documents = BuildDocumentRecords(normalizedProjects, normalizedCodeNodes, identityMaps);
        var directories = BuildDirectoryRecords(documents, identityMaps);
        var directoryIdsByPath = directories.ToDictionary(
            static directory => directory.Path,
            static directory => directory.Id,
            GetPathComparer());
        var documentsByRelativePath = documents.ToDictionary(
            static document => document.RelativePath,
            static document => document.Id,
            GetPathComparer());
        var directoryClosures = BuildDirectoryClosures(directories, directoryIdsByPath);
        var graphNodes = BuildGraphNodeRecords(normalizedProjects, normalizedCodeNodes, identityMaps);
        var graphNodeIdsByCanonicalId = graphNodes.ToDictionary(
            static graphNode => graphNode.CanonicalId,
            static graphNode => graphNode.Id,
            StringComparer.Ordinal);
        var projectNodes = normalizedProjects
            .Select(
                project => new ProjectNodeRecord
                {
                    Id = graphNodeIdsByCanonicalId[project.Id],
                    Name = project.Name,
                    ProjectDocumentId = documentsByRelativePath[project.RelativeFilePath],
                    ContentHash = project.ContentHash
                })
            .ToArray();
        var codeNodes = normalizedCodeNodes
            .Select(
                codeNode => new CodeNodeRecord
                {
                    Id = graphNodeIdsByCanonicalId[codeNode.CanonicalId],
                    ProjectNodeId = ResolveProjectNodeId(codeNode.ProjectId, graphNodeIdsByCanonicalId),
                    DocumentId = documentsByRelativePath[codeNode.RelativeFilePath],
                    FullyQualifiedName = codeNode.FullyQualifiedName,
                    DisplayName = codeNode.DisplayName,
                    NodeType = codeNode.NodeType,
                    StartLine = codeNode.StartLine,
                    EndLine = codeNode.EndLine,
                    Summary = codeNode.Summary,
                    VectorEmbedding = codeNode.VectorEmbedding
                })
            .ToArray();
        var dependencyEdges = normalizedEdges
            .Where(edge =>
                graphNodeIdsByCanonicalId.ContainsKey(edge.CallerId) &&
                graphNodeIdsByCanonicalId.ContainsKey(edge.CalleeId))
            .Select(
                edge => new DependencyEdgeRecord
                {
                    CallerNodeId = graphNodeIdsByCanonicalId[edge.CallerId],
                    CalleeNodeId = graphNodeIdsByCanonicalId[edge.CalleeId],
                    EdgeType = edge.EdgeType
                })
            .DistinctBy(static edge => (edge.CallerNodeId, edge.CalleeNodeId, edge.EdgeType))
            .OrderBy(static edge => edge.CallerNodeId)
            .ThenBy(static edge => edge.CalleeNodeId)
            .ThenBy(static edge => edge.EdgeType)
            .ToArray();

        return new PersistedGraph(
            directories,
            directoryClosures,
            documents,
            graphNodes,
            projectNodes,
            codeNodes,
            dependencyEdges);
    }

    private static DocumentRecord[] BuildDocumentRecords(
        IReadOnlyCollection<IndexedProject> projects,
        IReadOnlyCollection<IndexedCodeNode> codeNodes,
        PersistedIdentityMaps identityMaps)
    {
        var projectPaths = projects
            .Select(static project => project.RelativeFilePath)
            .ToHashSet(GetPathComparer());
        var relativePaths = projectPaths
            .Concat(codeNodes.Select(static codeNode => codeNode.RelativeFilePath))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(GetPathComparer())
            .OrderBy(static path => path, GetPathComparer())
            .ToArray();
        var nextDocumentId = GetNextId(identityMaps.DocumentIdsByRelativePath.Values);
        var documents = new List<DocumentRecord>(relativePaths.Length);

        foreach (var relativePath in relativePaths)
        {
            var documentId = identityMaps.DocumentIdsByRelativePath.GetValueOrDefault(relativePath);
            if (documentId == 0)
            {
                documentId = nextDocumentId++;
            }

            documents.Add(
                new DocumentRecord
                {
                    Id = documentId,
                    DirectoryId = 0,
                    FileName = GetFileName(relativePath),
                    Extension = Path.GetExtension(relativePath),
                    RelativePath = relativePath,
                    Kind = GetDocumentKind(relativePath, projectPaths.Contains(relativePath))
                });
        }

        return documents.ToArray();
    }

    private static DirectoryRecord[] BuildDirectoryRecords(
        IEnumerable<DocumentRecord> documents,
        PersistedIdentityMaps identityMaps)
    {
        var pathComparer = GetPathComparer();
        var directoryPaths = new HashSet<string>(pathComparer) { string.Empty };

        foreach (var document in documents)
        {
            AddDirectoryPath(directoryPaths, GetDirectoryPath(document.RelativePath));
        }

        var orderedPaths = directoryPaths
            .OrderBy(static path => path.Count(static character => character == '/'))
            .ThenBy(static path => path, pathComparer)
            .ToArray();
        var nextDirectoryId = GetNextId(identityMaps.DirectoryIdsByPath.Values);
        var directoryIdsByPath = new Dictionary<string, int>(pathComparer);
        var directories = new List<DirectoryRecord>(orderedPaths.Length);

        foreach (var path in orderedPaths)
        {
            var directoryId = identityMaps.DirectoryIdsByPath.GetValueOrDefault(path);
            if (directoryId == 0)
            {
                directoryId = nextDirectoryId++;
            }

            directoryIdsByPath[path] = directoryId;
            var parentPath = GetParentDirectoryPath(path);
            directories.Add(
                new DirectoryRecord
                {
                    Id = directoryId,
                    ParentId = parentPath is null ? null : directoryIdsByPath[parentPath],
                    Path = path,
                    Name = string.IsNullOrEmpty(path) ? "/" : GetFileName(path)
                });
        }

        foreach (var document in documents)
        {
            document.DirectoryId = directoryIdsByPath[GetDirectoryPath(document.RelativePath)];
        }

        return directories
            .OrderBy(static directory => directory.Path, pathComparer)
            .ToArray();
    }

    private static DirectoryClosureRecord[] BuildDirectoryClosures(
        IReadOnlyCollection<DirectoryRecord> directories,
        IReadOnlyDictionary<string, int> directoryIdsByPath)
    {
        var closures = new List<DirectoryClosureRecord>(directories.Count * 2);

        foreach (var directory in directories)
        {
            closures.Add(
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = directory.Id,
                    DescendantDirectoryId = directory.Id,
                    Depth = 0
                });

            var ancestorPath = GetParentDirectoryPath(directory.Path);
            var depth = 1;
            while (ancestorPath is not null)
            {
                closures.Add(
                    new DirectoryClosureRecord
                    {
                        AncestorDirectoryId = directoryIdsByPath[ancestorPath],
                        DescendantDirectoryId = directory.Id,
                        Depth = depth
                    });
                ancestorPath = GetParentDirectoryPath(ancestorPath);
                depth++;
            }
        }

        return closures
            .OrderBy(static closure => closure.AncestorDirectoryId)
            .ThenBy(static closure => closure.DescendantDirectoryId)
            .ToArray();
    }

    private static GraphNodeRecord[] BuildGraphNodeRecords(
        IReadOnlyCollection<IndexedProject> projects,
        IReadOnlyCollection<IndexedCodeNode> codeNodes,
        PersistedIdentityMaps identityMaps)
    {
        var nextGraphNodeId = GetNextId(identityMaps.GraphNodeIdsByCanonicalId.Values);
        var graphNodes = projects
            .Select(
                project => new GraphNodeRecord
                {
                    Id = identityMaps.GraphNodeIdsByCanonicalId.GetValueOrDefault(project.Id),
                    CanonicalId = project.Id,
                    Kind = GraphNodeKind.Project
                })
            .Concat(
                codeNodes.Select(
                    codeNode => new GraphNodeRecord
                    {
                        Id = identityMaps.GraphNodeIdsByCanonicalId.GetValueOrDefault(codeNode.CanonicalId),
                        CanonicalId = codeNode.CanonicalId,
                        Kind = GraphNodeKind.Code
                    }))
            .OrderBy(static graphNode => graphNode.CanonicalId, StringComparer.Ordinal)
            .ToArray();

        foreach (var graphNode in graphNodes)
        {
            if (graphNode.Id == 0)
            {
                graphNode.Id = nextGraphNodeId++;
            }
        }

        return graphNodes;
    }

    private static async Task<PersistedIdentityMaps> LoadIdentityMaps(
        SharpSenseDbContext context,
        CancellationToken ct)
    {
        var pathComparer = GetPathComparer();
        var persistedDirectories = await context.Directories
            .AsNoTracking()
            .Select(static directory => new { directory.Path, directory.Id })
            .ToArrayAsync(ct);
        var persistedDocuments = await context.Documents
            .AsNoTracking()
            .Select(static document => new { document.RelativePath, document.Id })
            .ToArrayAsync(ct);
        var persistedGraphNodes = await context.GraphNodes
            .AsNoTracking()
            .Select(static graphNode => new { graphNode.CanonicalId, graphNode.Id })
            .ToArrayAsync(ct);

        return new PersistedIdentityMaps(
            persistedDirectories.ToDictionary(static directory => directory.Path, static directory => directory.Id, pathComparer),
            persistedDocuments.ToDictionary(static document => document.RelativePath, static document => document.Id, pathComparer),
            persistedGraphNodes.ToDictionary(static graphNode => graphNode.CanonicalId, static graphNode => graphNode.Id, StringComparer.Ordinal));
    }

    private static async Task ReplacePersistedGraph(
        SharpSenseDbContext context,
        PersistedGraph graph,
        CancellationToken ct)
    {
        await context.DependencyEdges.ExecuteDeleteAsync(ct);
        await context.CodeNodes.ExecuteDeleteAsync(ct);
        await context.ProjectNodes.ExecuteDeleteAsync(ct);
        await context.GraphNodes.ExecuteDeleteAsync(ct);
        await context.Documents.ExecuteDeleteAsync(ct);
        await context.DirectoryClosures.ExecuteDeleteAsync(ct);
        await context.Directories.ExecuteDeleteAsync(ct);
        context.ChangeTracker.Clear();

        if (graph.Directories.Length > 0)
        {
            await context.Directories.AddRangeAsync(graph.Directories, ct);
        }

        if (graph.DirectoryClosures.Length > 0)
        {
            await context.DirectoryClosures.AddRangeAsync(graph.DirectoryClosures, ct);
        }

        if (graph.Documents.Length > 0)
        {
            await context.Documents.AddRangeAsync(graph.Documents, ct);
        }

        if (graph.GraphNodes.Length > 0)
        {
            await context.GraphNodes.AddRangeAsync(graph.GraphNodes, ct);
        }

        if (graph.ProjectNodes.Length > 0)
        {
            await context.ProjectNodes.AddRangeAsync(graph.ProjectNodes, ct);
        }

        if (graph.CodeNodes.Length > 0)
        {
            await context.CodeNodes.AddRangeAsync(graph.CodeNodes, ct);
        }

        if (graph.DependencyEdges.Length > 0)
        {
            await context.DependencyEdges.AddRangeAsync(graph.DependencyEdges, ct);
        }

        await context.SaveChangesAsync(ct);
        await RefreshSearchIndex(context, ct);
    }

    private static async Task RefreshSearchIndex(
        SharpSenseDbContext context,
        CancellationToken ct)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM CodeNodeSearch;
            INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath)
            SELECT CodeNodes.Id, GraphNodes.CanonicalId, CodeNodes.DisplayName, CodeNodes.FullyQualifiedName, CodeNodes.Summary, Documents.RelativePath
            FROM CodeNodes
            INNER JOIN GraphNodes ON GraphNodes.Id = CodeNodes.Id
            INNER JOIN Documents ON Documents.Id = CodeNodes.DocumentId;
            """,
            ct);
    }

    private static void AddTraceCounts(
        SharpSenseTraceSpan trace,
        PersistedGraph graph,
        int diagnosticCount)
    {
        trace.AddTag("index.project.count", graph.ProjectNodes.Length);
        trace.AddTag("index.code_node.count", graph.CodeNodes.Length);
        trace.AddTag("index.document.count", graph.Documents.Length);
        trace.AddTag("index.directory.count", graph.Directories.Length);
        trace.AddTag("index.graph_node.count", graph.GraphNodes.Length);
        trace.AddTag("index.dependency.count", graph.DependencyEdges.Length);
        trace.AddTag("index.diagnostic.count", diagnosticCount);
    }

    private static void AddDirectoryPath(
        ISet<string> directoryPaths,
        string directoryPath)
    {
        var currentPath = directoryPath;

        while (true)
        {
            directoryPaths.Add(currentPath);
            var parentPath = GetParentDirectoryPath(currentPath);
            if (parentPath is null)
            {
                return;
            }

            currentPath = parentPath;
        }
    }

    private static string NormalizeRelativePath(string path)
        => path.Trim().Replace('\\', '/').Trim('/');

    private static string GetDirectoryPath(string relativePath)
    {
        var separatorIndex = relativePath.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : relativePath[..separatorIndex];
    }

    private static string? GetParentDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            return null;
        }

        var separatorIndex = directoryPath.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : directoryPath[..separatorIndex];
    }

    private static string GetFileName(string relativePath)
    {
        var separatorIndex = relativePath.LastIndexOf('/');
        return separatorIndex < 0 ? relativePath : relativePath[(separatorIndex + 1)..];
    }

    private static DocumentKind GetDocumentKind(string relativePath, bool isProjectDocument)
    {
        if (isProjectDocument)
        {
            return DocumentKind.ProjectFile;
        }

        var extension = Path.GetExtension(relativePath);
        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".mdown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".mkd", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentKind.Markdown;
        }

        return extension switch
        {
            ".cs" or ".ts" or ".tsx" or ".js" or ".jsx" => DocumentKind.Source,
            _ => DocumentKind.Other
        };
    }

    private static int GetNextId(IEnumerable<int> ids)
        => ids.DefaultIfEmpty().Max() + 1;

    private static int? ResolveProjectNodeId(
        string? projectId,
        IReadOnlyDictionary<string, int> graphNodeIdsByCanonicalId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        return graphNodeIdsByCanonicalId.TryGetValue(projectId, out var projectNodeId)
            ? projectNodeId
            : null;
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record PersistedIdentityMaps(
        IReadOnlyDictionary<string, int> DirectoryIdsByPath,
        IReadOnlyDictionary<string, int> DocumentIdsByRelativePath,
        IReadOnlyDictionary<string, int> GraphNodeIdsByCanonicalId);

    private sealed record PersistedGraph(
        DirectoryRecord[] Directories,
        DirectoryClosureRecord[] DirectoryClosures,
        DocumentRecord[] Documents,
        GraphNodeRecord[] GraphNodes,
        ProjectNodeRecord[] ProjectNodes,
        CodeNodeRecord[] CodeNodes,
        DependencyEdgeRecord[] DependencyEdges);
}
