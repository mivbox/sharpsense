using Microsoft.EntityFrameworkCore;
using Serilog;
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
    private static readonly ILogger _logger = Log.ForContext<KnowledgeGraphRepository>();

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
            var normalizedExtractedNodes = CanonicalizeSnapshot(extractedNodes);
            var currentSnapshot = await LoadCurrentSnapshot(context, ct);
            if (AreEquivalentSnapshots(currentSnapshot, normalizedExtractedNodes))
            {
                trace.AddTag("index.persist.skipped", true);
                _logger.Information("Skipping full index persistence because no graph changes were detected.");
                return;
            }

            _logger.Debug(
                "Full index persistence required because {SnapshotDifference}",
                DescribeSnapshotDifference(currentSnapshot, normalizedExtractedNodes));

            var identityMaps = await LoadIdentityMaps(context, ct);
            var persistedGraph = BuildPersistedGraph(normalizedExtractedNodes, identityMaps);

            AddTraceCounts(trace, persistedGraph, normalizedExtractedNodes.Diagnostics.Count);
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
            var identityMaps = await LoadIdentityMaps(context, ct);
            var normalizedExtractedNodes = CanonicalizeSnapshot(
                extractedNodes,
                BuildKnownNodeIds(extractedNodes, identityMaps));
            var currentSnapshot = await LoadWorkspaceFilesSnapshot(context, changedFilePaths, ct);
            if (AreEquivalentSnapshots(currentSnapshot, normalizedExtractedNodes))
            {
                trace.AddTag("index.persist.skipped", true);
                _logger.Information(
                    "Skipping incremental index persistence because {ChangedFileCount} file(s) produced no graph changes.",
                    changedFilePaths.Length);
                return;
            }

            _logger.Debug(
                "Incremental index persistence required because {SnapshotDifference}",
                DescribeSnapshotDifference(currentSnapshot, normalizedExtractedNodes));

            await ReplaceWorkspaceFilesIncremental(context, changedFilePaths, currentSnapshot, normalizedExtractedNodes, identityMaps, ct);
            trace.AddTag("index.project.count", normalizedExtractedNodes.Projects.Count);
            trace.AddTag("index.code_node.count", normalizedExtractedNodes.CodeNodes.Count);
            trace.AddTag("index.dependency.count", normalizedExtractedNodes.Edges.Count);
            trace.AddTag("index.diagnostic.count", normalizedExtractedNodes.Diagnostics.Count);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception)
        {
            trace.RecordExceptionAndErrorStatus(exception);
            throw;
        }
    }

    public async Task<IReadOnlyList<IndexedCodeNode>> GetPersistedCodeNodes(CancellationToken ct)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        return await LoadPersistedCodeNodes(context, relativeFilePaths: null, ct);
    }

    public async Task<IReadOnlyList<IndexedCodeNode>> GetPersistedCodeNodes(
        IReadOnlyList<string> relativeFilePaths,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(relativeFilePaths);

        var normalizedPaths = NormalizeRelativePaths(relativeFilePaths);
        if (normalizedPaths.Length == 0)
        {
            return [];
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        return await LoadPersistedCodeNodes(context, normalizedPaths, ct);
    }

    public async Task<IReadOnlyList<string>> GetPersistedDocumentPathsUnderDirectory(
        string relativeDirectoryPath,
        CancellationToken ct)
    {
        var normalizedDirectoryPath = NormalizeDirectoryPath(relativeDirectoryPath);
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var documentQuery = context.Documents
            .AsNoTracking()
            .Select(static document => document.RelativePath);

        if (!string.IsNullOrEmpty(normalizedDirectoryPath))
        {
            var directoryPrefix = normalizedDirectoryPath + "/";
            documentQuery = documentQuery.Where(relativePath => relativePath.StartsWith(directoryPrefix));
        }

        return
        [
            .. await documentQuery
                .OrderBy(static relativePath => relativePath)
                .ToArrayAsync(ct)
        ];
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
                    edge.EdgeType))
            .ToArrayAsync(ct))
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .ToArray();

        return new ExtractedNodes(projects, codeNodes, edges, []);
    }

    private static async Task<ExtractedNodes> LoadWorkspaceFilesSnapshot(
        SharpSenseDbContext context,
        IReadOnlyList<string> relativeFilePaths,
        CancellationToken ct)
    {
        var normalizedPaths = NormalizeRelativePaths(relativeFilePaths);
        if (normalizedPaths.Length == 0)
        {
            return new ExtractedNodes([], [], [], []);
        }

        var pathComparer = GetPathComparer();
        var relativePathSet = normalizedPaths.ToHashSet(pathComparer);
        var documentIds = await context.Documents
            .AsNoTracking()
            .Where(document => relativePathSet.Contains(document.RelativePath))
            .Select(static document => document.Id)
            .ToArrayAsync(ct);
        if (documentIds.Length == 0)
        {
            return new ExtractedNodes([], [], [], []);
        }

        var codeNodes = await LoadPersistedCodeNodes(context, normalizedPaths, ct);
        var callerNodeIds = await context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => documentIds.Contains(codeNode.DocumentId))
            .Select(static codeNode => codeNode.Id)
            .ToArrayAsync(ct);
        var edges = callerNodeIds.Length == 0
            ? []
            : (await CodeNodeNavigationQueries.ProjectDependencyEdges(
                        context,
                        context.DependencyEdges
                            .AsNoTracking()
                            .Where(edge => callerNodeIds.Contains(edge.CallerNodeId))
                            .OrderBy(static edge => edge.CallerNodeId)
                            .ThenBy(static edge => edge.CalleeNodeId)
                            .ThenBy(static edge => edge.EdgeType))
                    .Select(
                        static edge => new IndexedDependency(
                            edge.CallerId,
                            edge.CalleeId,
                            edge.EdgeType))
                    .ToArrayAsync(ct))
                .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
                .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
                .ThenBy(static edge => edge.EdgeType)
                .ToArray();

        return new ExtractedNodes([], codeNodes, edges, []);
    }

    private static async Task<IReadOnlyList<IndexedCodeNode>> LoadPersistedCodeNodes(
        SharpSenseDbContext context,
        IReadOnlyList<string>? relativeFilePaths,
        CancellationToken ct)
    {
        IQueryable<CodeNodeRecord> codeNodeQuery = context.CodeNodes
            .AsNoTracking()
            .OrderBy(static codeNode => codeNode.FullyQualifiedName)
            .ThenBy(static codeNode => codeNode.Id);
        if (relativeFilePaths is { Count: > 0 })
        {
            var normalizedPaths = relativeFilePaths.ToHashSet(GetPathComparer());
            var documentIds = await context.Documents
                .AsNoTracking()
                .Where(document => normalizedPaths.Contains(document.RelativePath))
                .Select(static document => document.Id)
                .ToArrayAsync(ct);
            if (documentIds.Length == 0)
            {
                return [];
            }

            codeNodeQuery = codeNodeQuery.Where(codeNode => documentIds.Contains(codeNode.DocumentId));
        }

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

    private static ExtractedNodes CanonicalizeSnapshot(
        ExtractedNodes extractedNodes,
        IReadOnlySet<string>? knownNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(extractedNodes);

        var normalizedProjects = extractedNodes.Projects
            .Select(
                static project => project with
                {
                    RelativeFilePath = NormalizeRelativePath(project.RelativeFilePath)
                })
            .GroupBy(static project => project.Id, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ThenBy(static project => project.Id, StringComparer.Ordinal)
            .ToArray();
        var normalizedCodeNodes = extractedNodes.CodeNodes
            .Select(
                static codeNode => codeNode with
                {
                    RelativeFilePath = NormalizeRelativePath(codeNode.RelativeFilePath)
                })
            .GroupBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .ToArray();
        var callerNodeIds = normalizedProjects
            .Select(static project => project.Id)
            .Concat(normalizedCodeNodes.Select(static codeNode => codeNode.CanonicalId))
            .ToHashSet(StringComparer.Ordinal);
        var persistedNodeIds = knownNodeIds ?? callerNodeIds;
        var normalizedEdges = extractedNodes.Edges
            .Where(edge => callerNodeIds.Contains(edge.CallerId) && persistedNodeIds.Contains(edge.CalleeId))
            .GroupBy(static edge => (edge.CallerId, edge.CalleeId, edge.EdgeType))
            .Select(static group => group.Last())
            .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.EdgeType)
            .ToArray();

        return new ExtractedNodes(
            normalizedProjects,
            normalizedCodeNodes,
            normalizedEdges,
            extractedNodes.Diagnostics);
    }

    private static HashSet<string> BuildKnownNodeIds(
        ExtractedNodes extractedNodes,
        PersistedIdentityMaps identityMaps)
    {
        var knownNodeIds = identityMaps.GraphNodeIdsByCanonicalId.Keys
            .ToHashSet(StringComparer.Ordinal);
        knownNodeIds.UnionWith(extractedNodes.Projects.Select(static project => project.Id));
        knownNodeIds.UnionWith(extractedNodes.CodeNodes.Select(static codeNode => codeNode.CanonicalId));
        return knownNodeIds;
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
                    SearchText = codeNode.SearchText,
                    BodyHash = codeNode.BodyHash,
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

    private static async Task ReplaceWorkspaceFilesIncremental(
        SharpSenseDbContext context,
        IReadOnlyList<string> changedFilePaths,
        ExtractedNodes currentSnapshot,
        ExtractedNodes updatedSnapshot,
        PersistedIdentityMaps identityMaps,
        CancellationToken ct)
    {
        var pathComparer = GetPathComparer();
        var normalizedChangedPaths = NormalizeRelativePaths(changedFilePaths);
        var currentCodeNodesByCanonicalId = currentSnapshot.CodeNodes.ToDictionary(
            static codeNode => codeNode.CanonicalId,
            StringComparer.Ordinal);
        var updatedCodeNodesByCanonicalId = updatedSnapshot.CodeNodes.ToDictionary(
            static codeNode => codeNode.CanonicalId,
            StringComparer.Ordinal);
        var currentPaths = currentSnapshot.CodeNodes
            .Select(static codeNode => codeNode.RelativeFilePath)
            .Distinct(pathComparer)
            .ToHashSet(pathComparer);
        var updatedPaths = updatedSnapshot.CodeNodes
            .Select(static codeNode => codeNode.RelativeFilePath)
            .Distinct(pathComparer)
            .ToHashSet(pathComparer);
        var graphNodeIdsByCanonicalId = new Dictionary<string, int>(
            identityMaps.GraphNodeIdsByCanonicalId,
            StringComparer.Ordinal);
        var currentCodeNodeIds = currentSnapshot.CodeNodes
            .Select(codeNode => graphNodeIdsByCanonicalId[codeNode.CanonicalId])
            .ToArray();
        var codeNodeRecordsById = currentCodeNodeIds.Length == 0
            ? new Dictionary<int, CodeNodeRecord>()
            : (await context.CodeNodes
                    .Where(codeNode => currentCodeNodeIds.Contains(codeNode.Id))
                    .ToArrayAsync(ct))
                .ToDictionary(static codeNode => codeNode.Id);
        var currentDocuments = await context.Documents
            .Where(document => normalizedChangedPaths.Contains(document.RelativePath))
            .ToArrayAsync(ct);
        var documentsByPath = currentDocuments.ToDictionary(
            static document => document.RelativePath,
            pathComparer);
        var directories = await context.Directories
            .ToArrayAsync(ct);
        var directoriesByPath = directories.ToDictionary(
            static directory => directory.Path,
            pathComparer);
        var nextDirectoryId = GetNextId(directories.Select(static directory => directory.Id));
        var nextDocumentId = GetNextId(identityMaps.DocumentIdsByRelativePath.Values);
        var nextGraphNodeId = GetNextId(graphNodeIdsByCanonicalId.Values);

        await EnsureDirectories(context, updatedPaths, directoriesByPath, nextDirectoryId, ct);
        nextDirectoryId = GetNextId(directoriesByPath.Values.Select(static directory => directory.Id));

        foreach (var updatedPath in updatedPaths.OrderBy(static path => path, pathComparer))
        {
            if (documentsByPath.ContainsKey(updatedPath))
            {
                continue;
            }

            var directoryId = directoriesByPath[GetDirectoryPath(updatedPath)].Id;
            var documentId = identityMaps.DocumentIdsByRelativePath.GetValueOrDefault(updatedPath);
            if (documentId == 0)
            {
                documentId = nextDocumentId++;
            }

            var document = new DocumentRecord
            {
                Id = documentId,
                DirectoryId = directoryId,
                FileName = GetFileName(updatedPath),
                Extension = Path.GetExtension(updatedPath),
                RelativePath = updatedPath,
                Kind = GetDocumentKind(updatedPath, isProjectDocument: false)
            };
            documentsByPath[updatedPath] = document;
            await context.Documents.AddAsync(document, ct);
        }

        var modifiedCodeNodeIds = new HashSet<int>();

        foreach (var updatedCodeNode in updatedSnapshot.CodeNodes)
        {
            if (!documentsByPath.TryGetValue(updatedCodeNode.RelativeFilePath, out var document))
            {
                throw new InvalidOperationException($"Document '{updatedCodeNode.RelativeFilePath}' must exist before persisting code nodes.");
            }

            var graphNodeId = graphNodeIdsByCanonicalId.GetValueOrDefault(updatedCodeNode.CanonicalId);
            if (graphNodeId == 0)
            {
                graphNodeId = nextGraphNodeId++;
                graphNodeIdsByCanonicalId[updatedCodeNode.CanonicalId] = graphNodeId;
                await context.GraphNodes.AddAsync(
                    new GraphNodeRecord
                    {
                        Id = graphNodeId,
                        CanonicalId = updatedCodeNode.CanonicalId,
                        Kind = GraphNodeKind.Code
                    },
                    ct);
            }

            var projectNodeId = ResolveProjectNodeId(updatedCodeNode.ProjectId, graphNodeIdsByCanonicalId);
            if (!codeNodeRecordsById.TryGetValue(graphNodeId, out var codeNodeRecord))
            {
                codeNodeRecord = CreateCodeNodeRecord(updatedCodeNode, graphNodeId, projectNodeId, document.Id);
                codeNodeRecordsById[graphNodeId] = codeNodeRecord;
                modifiedCodeNodeIds.Add(graphNodeId);
                await context.CodeNodes.AddAsync(codeNodeRecord, ct);
                continue;
            }

            if (!ApplyCodeNodeChanges(codeNodeRecord, updatedCodeNode, projectNodeId, document.Id))
            {
                continue;
            }

            modifiedCodeNodeIds.Add(graphNodeId);
        }

        var removedCodeNodeIds = currentCodeNodesByCanonicalId.Keys
            .Where(canonicalId => !updatedCodeNodesByCanonicalId.ContainsKey(canonicalId))
            .Select(canonicalId => graphNodeIdsByCanonicalId[canonicalId])
            .ToArray();
        var currentChangedCallerIds = currentSnapshot.CodeNodes
            .Select(codeNode => graphNodeIdsByCanonicalId[codeNode.CanonicalId])
            .ToArray();
        if (currentChangedCallerIds.Length > 0 || removedCodeNodeIds.Length > 0)
        {
            await context.DependencyEdges
                .Where(edge =>
                    currentChangedCallerIds.Contains(edge.CallerNodeId) ||
                    removedCodeNodeIds.Contains(edge.CalleeNodeId))
                .ExecuteDeleteAsync(ct);
        }

        var dependencyEdges = updatedSnapshot.Edges
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
            .ToArray();
        if (dependencyEdges.Length > 0)
        {
            await context.DependencyEdges.AddRangeAsync(dependencyEdges, ct);
        }

        if (removedCodeNodeIds.Length > 0)
        {
            var removedCodeNodes = codeNodeRecordsById
                .Where(entry => removedCodeNodeIds.Contains(entry.Key))
                .Select(static entry => entry.Value)
                .ToArray();
            if (removedCodeNodes.Length > 0)
            {
                context.CodeNodes.RemoveRange(removedCodeNodes);
            }

            var removedGraphNodes = await context.GraphNodes
                .Where(graphNode => removedCodeNodeIds.Contains(graphNode.Id))
                .ToArrayAsync(ct);
            if (removedGraphNodes.Length > 0)
            {
                context.GraphNodes.RemoveRange(removedGraphNodes);
            }
        }

        await context.SaveChangesAsync(ct);

        var orphanDocuments = await context.Documents
            .Where(document => normalizedChangedPaths.Contains(document.RelativePath))
            .Where(document =>
                !context.CodeNodes.Any(codeNode => codeNode.DocumentId == document.Id) &&
                !context.ProjectNodes.Any(projectNode => projectNode.ProjectDocumentId == document.Id))
            .ToArrayAsync(ct);
        if (orphanDocuments.Length > 0)
        {
            context.Documents.RemoveRange(orphanDocuments);
            await context.SaveChangesAsync(ct);
        }

        await PruneDirectories(context, currentPaths, updatedPaths, ct);
        await RefreshSearchIndexDelta(context, modifiedCodeNodeIds, removedCodeNodeIds, ct);
    }

    private static async Task EnsureDirectories(
        SharpSenseDbContext context,
        IReadOnlyCollection<string> relativeFilePaths,
        IDictionary<string, DirectoryRecord> directoriesByPath,
        int nextDirectoryId,
        CancellationToken ct)
    {
        if (relativeFilePaths.Count == 0)
        {
            return;
        }

        var pathComparer = GetPathComparer();
        var requiredDirectoryPaths = new HashSet<string>(pathComparer) { string.Empty };
        foreach (var relativeFilePath in relativeFilePaths)
        {
            AddDirectoryPath(requiredDirectoryPaths, GetDirectoryPath(relativeFilePath));
        }

        var missingPaths = requiredDirectoryPaths
            .Where(path => !directoriesByPath.ContainsKey(path))
            .OrderBy(static path => path.Count(static character => character == '/'))
            .ThenBy(static path => path, pathComparer)
            .ToArray();
        if (missingPaths.Length == 0)
        {
            return;
        }

        var newDirectories = new List<DirectoryRecord>(missingPaths.Length);
        var newClosures = new List<DirectoryClosureRecord>(missingPaths.Length * 2);

        foreach (var path in missingPaths)
        {
            var parentPath = GetParentDirectoryPath(path);
            var directory = new DirectoryRecord
            {
                Id = nextDirectoryId++,
                ParentId = parentPath is null ? null : directoriesByPath[parentPath].Id,
                Path = path,
                Name = string.IsNullOrEmpty(path) ? "/" : GetFileName(path)
            };

            directoriesByPath[path] = directory;
            newDirectories.Add(directory);
            newClosures.Add(new DirectoryClosureRecord
            {
                AncestorDirectoryId = directory.Id,
                DescendantDirectoryId = directory.Id,
                Depth = 0
            });

            var ancestorPath = parentPath;
            var depth = 1;
            while (ancestorPath is not null)
            {
                newClosures.Add(
                    new DirectoryClosureRecord
                    {
                        AncestorDirectoryId = directoriesByPath[ancestorPath].Id,
                        DescendantDirectoryId = directory.Id,
                        Depth = depth
                    });
                ancestorPath = GetParentDirectoryPath(ancestorPath);
                depth++;
            }
        }

        await context.Directories.AddRangeAsync(newDirectories, ct);
        await context.DirectoryClosures.AddRangeAsync(newClosures, ct);
    }

    private static async Task PruneDirectories(
        SharpSenseDbContext context,
        IReadOnlySet<string> currentPaths,
        IReadOnlySet<string> updatedPaths,
        CancellationToken ct)
    {
        var removedPaths = currentPaths
            .Except(updatedPaths, GetPathComparer())
            .ToArray();
        if (removedPaths.Length == 0)
        {
            return;
        }

        var pathComparer = GetPathComparer();
        var candidateDirectoryPaths = new HashSet<string>(pathComparer);
        foreach (var removedPath in removedPaths)
        {
            AddDirectoryPath(candidateDirectoryPaths, GetDirectoryPath(removedPath));
        }

        candidateDirectoryPaths.Remove(string.Empty);
        if (candidateDirectoryPaths.Count == 0)
        {
            return;
        }

        var allDirectories = await context.Directories
            .ToArrayAsync(ct);
        var documentDirectoryIds = await context.Documents
            .AsNoTracking()
            .Select(static document => document.DirectoryId)
            .Distinct()
            .ToArrayAsync(ct);
        var remainingDirectoryIds = allDirectories
            .Select(static directory => directory.Id)
            .ToHashSet();
        var candidateDirectories = allDirectories
            .Where(directory => candidateDirectoryPaths.Contains(directory.Path))
            .OrderByDescending(static directory => directory.Path.Count(static character => character == '/'))
            .ThenBy(static directory => directory.Path, pathComparer)
            .ToArray();

        foreach (var directory in candidateDirectories)
        {
            if (!remainingDirectoryIds.Contains(directory.Id))
            {
                continue;
            }

            var hasDocuments = documentDirectoryIds.Contains(directory.Id);
            var hasChildren = allDirectories.Any(candidate =>
                candidate.ParentId == directory.Id &&
                remainingDirectoryIds.Contains(candidate.Id));
            if (hasDocuments || hasChildren)
            {
                continue;
            }

            context.Directories.Remove(directory);
            remainingDirectoryIds.Remove(directory.Id);
        }

        await context.SaveChangesAsync(ct);
    }

    private static CodeNodeRecord CreateCodeNodeRecord(
        IndexedCodeNode codeNode,
        int id,
        int? projectNodeId,
        int documentId)
    {
        return new CodeNodeRecord
        {
            Id = id,
            ProjectNodeId = projectNodeId,
            DocumentId = documentId,
            FullyQualifiedName = codeNode.FullyQualifiedName,
            DisplayName = codeNode.DisplayName,
            NodeType = codeNode.NodeType,
            StartLine = codeNode.StartLine,
            EndLine = codeNode.EndLine,
            Summary = codeNode.Summary,
            SearchText = codeNode.SearchText,
            BodyHash = codeNode.BodyHash,
            VectorEmbedding = codeNode.VectorEmbedding
        };
    }

    private static bool ApplyCodeNodeChanges(
        CodeNodeRecord codeNodeRecord,
        IndexedCodeNode updatedCodeNode,
        int? projectNodeId,
        int documentId)
    {
        if (codeNodeRecord.ProjectNodeId == projectNodeId &&
            codeNodeRecord.DocumentId == documentId &&
            string.Equals(codeNodeRecord.FullyQualifiedName, updatedCodeNode.FullyQualifiedName, StringComparison.Ordinal) &&
            string.Equals(codeNodeRecord.DisplayName, updatedCodeNode.DisplayName, StringComparison.Ordinal) &&
            codeNodeRecord.NodeType == updatedCodeNode.NodeType &&
            codeNodeRecord.StartLine == updatedCodeNode.StartLine &&
            codeNodeRecord.EndLine == updatedCodeNode.EndLine &&
            string.Equals(codeNodeRecord.Summary, updatedCodeNode.Summary, StringComparison.Ordinal) &&
            string.Equals(codeNodeRecord.SearchText, updatedCodeNode.SearchText, StringComparison.Ordinal) &&
            string.Equals(codeNodeRecord.BodyHash, updatedCodeNode.BodyHash, StringComparison.Ordinal) &&
            VectorsEqual(codeNodeRecord.VectorEmbedding, updatedCodeNode.VectorEmbedding))
        {
            return false;
        }

        codeNodeRecord.ProjectNodeId = projectNodeId;
        codeNodeRecord.DocumentId = documentId;
        codeNodeRecord.FullyQualifiedName = updatedCodeNode.FullyQualifiedName;
        codeNodeRecord.DisplayName = updatedCodeNode.DisplayName;
        codeNodeRecord.NodeType = updatedCodeNode.NodeType;
        codeNodeRecord.StartLine = updatedCodeNode.StartLine;
        codeNodeRecord.EndLine = updatedCodeNode.EndLine;
        codeNodeRecord.Summary = updatedCodeNode.Summary;
        codeNodeRecord.SearchText = updatedCodeNode.SearchText;
        codeNodeRecord.BodyHash = updatedCodeNode.BodyHash;
        codeNodeRecord.VectorEmbedding = updatedCodeNode.VectorEmbedding;
        return true;
    }

    private static async Task RefreshSearchIndexDelta(
        SharpSenseDbContext context,
        IReadOnlyCollection<int> modifiedCodeNodeIds,
        IReadOnlyCollection<int> removedCodeNodeIds,
        CancellationToken ct)
    {
        var idsToDelete = modifiedCodeNodeIds
            .Concat(removedCodeNodeIds)
            .Distinct()
            .OrderBy(static id => id)
            .ToArray();
        if (idsToDelete.Length > 0)
        {
            var deleteSql = $"DELETE FROM CodeNodeSearch WHERE CAST(Id AS INTEGER) IN ({string.Join(", ", idsToDelete)});";
            await context.Database.ExecuteSqlRawAsync(deleteSql, ct);
        }

        var idsToInsert = modifiedCodeNodeIds
            .Distinct()
            .OrderBy(static id => id)
            .ToArray();
        if (idsToInsert.Length == 0)
        {
            return;
        }

        var insertSql =
            $$"""
            INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, SearchText, RelativeFilePath)
            SELECT CodeNodes.Id, GraphNodes.CanonicalId, CodeNodes.DisplayName, CodeNodes.FullyQualifiedName, CodeNodes.SearchText, Documents.RelativePath
            FROM CodeNodes
            INNER JOIN GraphNodes ON GraphNodes.Id = CodeNodes.Id
            INNER JOIN Documents ON Documents.Id = CodeNodes.DocumentId
            WHERE CodeNodes.Id IN ({{string.Join(", ", idsToInsert)}});
            """;
        await context.Database.ExecuteSqlRawAsync(insertSql, ct);
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

    private static bool AreEquivalentSnapshots(
        ExtractedNodes currentSnapshot,
        ExtractedNodes updatedSnapshot)
    {
        return AreEquivalentProjects(currentSnapshot.Projects, updatedSnapshot.Projects) &&
               AreEquivalentCodeNodes(currentSnapshot.CodeNodes, updatedSnapshot.CodeNodes) &&
               AreEquivalentEdges(currentSnapshot.Edges, updatedSnapshot.Edges);
    }

    private static string DescribeSnapshotDifference(
        ExtractedNodes currentSnapshot,
        ExtractedNodes updatedSnapshot)
    {
        return DescribeProjectDifference(currentSnapshot.Projects, updatedSnapshot.Projects) ??
               DescribeCodeNodeDifference(currentSnapshot.CodeNodes, updatedSnapshot.CodeNodes) ??
               DescribeEdgeDifference(currentSnapshot.Edges, updatedSnapshot.Edges) ??
               "no difference detected";
    }

    private static bool AreEquivalentProjects(
        IReadOnlyList<IndexedProject> currentProjects,
        IReadOnlyList<IndexedProject> updatedProjects)
    {
        if (currentProjects.Count != updatedProjects.Count)
        {
            return false;
        }

        for (var index = 0; index < currentProjects.Count; index++)
        {
            if (!EqualityComparer<IndexedProject>.Default.Equals(currentProjects[index], updatedProjects[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string? DescribeProjectDifference(
        IReadOnlyList<IndexedProject> currentProjects,
        IReadOnlyList<IndexedProject> updatedProjects)
    {
        if (currentProjects.Count != updatedProjects.Count)
        {
            return $"project count differs: persisted={currentProjects.Count}, updated={updatedProjects.Count}";
        }

        for (var index = 0; index < currentProjects.Count; index++)
        {
            var currentProject = currentProjects[index];
            var updatedProject = updatedProjects[index];
            if (!string.Equals(currentProject.Id, updatedProject.Id, StringComparison.Ordinal))
            {
                return $"project[{index}] id differs: persisted='{currentProject.Id}', updated='{updatedProject.Id}'";
            }

            if (!string.Equals(currentProject.Name, updatedProject.Name, StringComparison.Ordinal))
            {
                return $"project[{index}] name differs: persisted='{currentProject.Name}', updated='{updatedProject.Name}'";
            }

            if (!string.Equals(currentProject.RelativeFilePath, updatedProject.RelativeFilePath, StringComparison.Ordinal))
            {
                return $"project[{index}] relative path differs: persisted='{currentProject.RelativeFilePath}', updated='{updatedProject.RelativeFilePath}'";
            }

            if (!string.Equals(currentProject.ContentHash, updatedProject.ContentHash, StringComparison.Ordinal))
            {
                return $"project[{index}] content hash differs: persisted='{currentProject.ContentHash}', updated='{updatedProject.ContentHash}'";
            }
        }

        return null;
    }

    private static bool AreEquivalentCodeNodes(
        IReadOnlyList<IndexedCodeNode> currentCodeNodes,
        IReadOnlyList<IndexedCodeNode> updatedCodeNodes)
    {
        if (currentCodeNodes.Count != updatedCodeNodes.Count)
        {
            return false;
        }

        for (var index = 0; index < currentCodeNodes.Count; index++)
        {
            var currentCodeNode = currentCodeNodes[index];
            var updatedCodeNode = updatedCodeNodes[index];
            if (!EqualityComparer<string>.Default.Equals(currentCodeNode.CanonicalId, updatedCodeNode.CanonicalId) ||
                !EqualityComparer<string?>.Default.Equals(currentCodeNode.ProjectId, updatedCodeNode.ProjectId) ||
                !EqualityComparer<string>.Default.Equals(currentCodeNode.FullyQualifiedName, updatedCodeNode.FullyQualifiedName) ||
                !EqualityComparer<string>.Default.Equals(currentCodeNode.DisplayName, updatedCodeNode.DisplayName) ||
                currentCodeNode.NodeType != updatedCodeNode.NodeType ||
                !EqualityComparer<string>.Default.Equals(currentCodeNode.RelativeFilePath, updatedCodeNode.RelativeFilePath) ||
                currentCodeNode.StartLine != updatedCodeNode.StartLine ||
                currentCodeNode.EndLine != updatedCodeNode.EndLine ||
                !EqualityComparer<string>.Default.Equals(currentCodeNode.Summary, updatedCodeNode.Summary) ||
                !EqualityComparer<string>.Default.Equals(currentCodeNode.SearchText, updatedCodeNode.SearchText) ||
                !EqualityComparer<string?>.Default.Equals(currentCodeNode.BodyHash, updatedCodeNode.BodyHash) ||
                !VectorsEqual(currentCodeNode.VectorEmbedding, updatedCodeNode.VectorEmbedding))
            {
                return false;
            }
        }

        return true;
    }

    private static string? DescribeCodeNodeDifference(
        IReadOnlyList<IndexedCodeNode> currentCodeNodes,
        IReadOnlyList<IndexedCodeNode> updatedCodeNodes)
    {
        if (currentCodeNodes.Count != updatedCodeNodes.Count)
        {
            return $"code node count differs: persisted={currentCodeNodes.Count}, updated={updatedCodeNodes.Count}";
        }

        for (var index = 0; index < currentCodeNodes.Count; index++)
        {
            var currentCodeNode = currentCodeNodes[index];
            var updatedCodeNode = updatedCodeNodes[index];
            if (!string.Equals(currentCodeNode.CanonicalId, updatedCodeNode.CanonicalId, StringComparison.Ordinal))
            {
                return $"code node[{index}] canonical id differs: persisted='{currentCodeNode.CanonicalId}', updated='{updatedCodeNode.CanonicalId}'";
            }

            if (!string.Equals(currentCodeNode.ProjectId, updatedCodeNode.ProjectId, StringComparison.Ordinal))
            {
                return $"code node[{index}] project id differs: persisted='{currentCodeNode.ProjectId}', updated='{updatedCodeNode.ProjectId}'";
            }

            if (!string.Equals(currentCodeNode.FullyQualifiedName, updatedCodeNode.FullyQualifiedName, StringComparison.Ordinal))
            {
                return $"code node[{index}] fully qualified name differs: persisted='{currentCodeNode.FullyQualifiedName}', updated='{updatedCodeNode.FullyQualifiedName}'";
            }

            if (!string.Equals(currentCodeNode.DisplayName, updatedCodeNode.DisplayName, StringComparison.Ordinal))
            {
                return $"code node[{index}] display name differs: persisted='{currentCodeNode.DisplayName}', updated='{updatedCodeNode.DisplayName}'";
            }

            if (currentCodeNode.NodeType != updatedCodeNode.NodeType)
            {
                return $"code node[{index}] node type differs: persisted='{currentCodeNode.NodeType}', updated='{updatedCodeNode.NodeType}'";
            }

            if (!string.Equals(currentCodeNode.RelativeFilePath, updatedCodeNode.RelativeFilePath, StringComparison.Ordinal))
            {
                return $"code node[{index}] relative path differs: persisted='{currentCodeNode.RelativeFilePath}', updated='{updatedCodeNode.RelativeFilePath}'";
            }

            if (currentCodeNode.StartLine != updatedCodeNode.StartLine)
            {
                return $"code node[{index}] start line differs: persisted={currentCodeNode.StartLine}, updated={updatedCodeNode.StartLine}";
            }

            if (currentCodeNode.EndLine != updatedCodeNode.EndLine)
            {
                return $"code node[{index}] end line differs: persisted={currentCodeNode.EndLine}, updated={updatedCodeNode.EndLine}";
            }

            if (!string.Equals(currentCodeNode.Summary, updatedCodeNode.Summary, StringComparison.Ordinal))
            {
                return $"code node[{index}] summary differs: persisted='{currentCodeNode.Summary}', updated='{updatedCodeNode.Summary}'";
            }

            if (!string.Equals(currentCodeNode.SearchText, updatedCodeNode.SearchText, StringComparison.Ordinal))
            {
                return $"code node[{index}] search text differs: persisted='{currentCodeNode.SearchText}', updated='{updatedCodeNode.SearchText}'";
            }

            if (!string.Equals(currentCodeNode.BodyHash, updatedCodeNode.BodyHash, StringComparison.Ordinal))
            {
                return $"code node[{index}] body hash differs: persisted='{currentCodeNode.BodyHash}', updated='{updatedCodeNode.BodyHash}'";
            }

            if (!VectorsEqual(currentCodeNode.VectorEmbedding, updatedCodeNode.VectorEmbedding))
            {
                return $"code node[{index}] vector differs";
            }
        }

        return null;
    }

    private static bool AreEquivalentEdges(
        IReadOnlyList<IndexedDependency> currentEdges,
        IReadOnlyList<IndexedDependency> updatedEdges)
    {
        if (currentEdges.Count != updatedEdges.Count)
        {
            return false;
        }

        for (var index = 0; index < currentEdges.Count; index++)
        {
            if (!EqualityComparer<IndexedDependency>.Default.Equals(currentEdges[index], updatedEdges[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string? DescribeEdgeDifference(
        IReadOnlyList<IndexedDependency> currentEdges,
        IReadOnlyList<IndexedDependency> updatedEdges)
    {
        if (currentEdges.Count != updatedEdges.Count)
        {
            return $"dependency edge count differs: persisted={currentEdges.Count}, updated={updatedEdges.Count}";
        }

        for (var index = 0; index < currentEdges.Count; index++)
        {
            var currentEdge = currentEdges[index];
            var updatedEdge = updatedEdges[index];
            if (!string.Equals(currentEdge.CallerId, updatedEdge.CallerId, StringComparison.Ordinal))
            {
                return $"edge[{index}] caller differs: persisted='{currentEdge.CallerId}', updated='{updatedEdge.CallerId}'";
            }

            if (!string.Equals(currentEdge.CalleeId, updatedEdge.CalleeId, StringComparison.Ordinal))
            {
                return $"edge[{index}] callee differs: persisted='{currentEdge.CalleeId}', updated='{updatedEdge.CalleeId}'";
            }

            if (currentEdge.EdgeType != updatedEdge.EdgeType)
            {
                return $"edge[{index}] type differs: persisted='{currentEdge.EdgeType}', updated='{updatedEdge.EdgeType}'";
            }
        }

        return null;
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

    private static string[] NormalizeRelativePaths(IEnumerable<string> paths)
        => paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeRelativePath)
            .Distinct(GetPathComparer())
            .OrderBy(static path => path, GetPathComparer())
            .ToArray();

    private static string NormalizeDirectoryPath(string path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : NormalizeRelativePath(path);

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

    private static bool VectorsEqual(
        float[]? currentVector,
        float[]? updatedVector)
    {
        return currentVector is null && updatedVector is null ||
               currentVector is not null && updatedVector is not null && currentVector.SequenceEqual(updatedVector);
    }

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
