using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Storage;
using static SharpSense.Infrastructure.Indexing.GraphPaths;

namespace SharpSense.Infrastructure.Indexing;

internal static class PersistedGraphBuilder
{
    internal static PersistedIdentityMaps MatchMovedProjectIdentities(
        ExtractedNodes current,
        ExtractedNodes updated,
        PersistedIdentityMaps identities)
    {
        var currentProjectIds = current.Projects
            .Select(project => project.Id)
            .ToHashSet(StringComparer.Ordinal);
        var updatedProjectIds = updated.Projects
            .Select(project => project.Id)
            .ToHashSet(StringComparer.Ordinal);
        var removedProjects = current.Projects
            .Where(project => !updatedProjectIds.Contains(project.Id))
            .ToArray();
        var addedProjects = updated.Projects
            .Where(project => !currentProjectIds.Contains(project.Id))
            .ToArray();
        var graphNodeIds = new Dictionary<string, int>(identities.GraphNodeIdsByCanonicalId, StringComparer.Ordinal);

        foreach (var added in addedProjects)
        {
            // A declaration name alone is never enough to move authored memories between projects.
            // Preserve an unambiguous project move only when project content and all declarations match.
            if (string.IsNullOrEmpty(added.ContentHash) ||
                addedProjects.Count(project => project.Name == added.Name && project.ContentHash == added.ContentHash) != 1)
            {
                continue;
            }

            var candidates = removedProjects
                .Where(project =>
                project.Name == added.Name && project.ContentHash == added.ContentHash)
                .ToArray();
            if (candidates.Length != 1)
            {
                continue;
            }

            var before = current.CodeNodes
                .Where(node => node.ProjectId == candidates[0].Id)
                .ToArray();
            var after = updated.CodeNodes
                .Where(node => node.ProjectId == added.Id)
                .ToArray();
            var beforeByDeclaration = before.ToLookup(node => (node.FullyQualifiedName, node.BodyHash));
            if (before.Length != after.Length || after
                .Any(node =>
                    string.IsNullOrEmpty(node.BodyHash) ||
                    beforeByDeclaration[(node.FullyQualifiedName, node.BodyHash)].Count() != 1))
            {
                continue;
            }

            foreach (var node in after)
            {
                var original = beforeByDeclaration[(node.FullyQualifiedName, node.BodyHash)].Single();
                graphNodeIds.TryAdd(node.CanonicalId, identities.GraphNodeIdsByCanonicalId[original.CanonicalId]);
            }
        }

        return identities with
        {
            GraphNodeIdsByCanonicalId = graphNodeIds
        };
    }

    internal static PersistedGraph BuildPersistedGraph(
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
        var directoryIdsByPath = directories
            .ToDictionary(
                static directory => directory.Path,
                static directory => directory.Id,
                FileSystemPaths.Comparer);
        var documentsByRelativePath = documents
            .ToDictionary(
                static document => document.RelativePath,
                static document => document.Id,
                FileSystemPaths.Comparer);
        var directoryClosures = BuildDirectoryClosures(directories, directoryIdsByPath);
        var graphNodes = BuildGraphNodeRecords(normalizedProjects, normalizedCodeNodes, normalizedEdges, identityMaps);
        var graphNodeIdsByCanonicalId = graphNodes
            .ToDictionary(
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
                    EdgeType = edge.EdgeType,
                    Metadata = edge.Metadata
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
            .ToHashSet(FileSystemPaths.Comparer);
        var relativePaths = projectPaths
            .Concat(codeNodes.Select(static codeNode => codeNode.RelativeFilePath))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(FileSystemPaths.Comparer)
            .OrderBy(static path => path, FileSystemPaths.Comparer)
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
        var pathComparer = FileSystemPaths.Comparer;
        var directoryPaths = new HashSet<string>(pathComparer)
        {
            string.Empty
        };

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
        IReadOnlyCollection<IndexedDependency> edges,
        PersistedIdentityMaps identityMaps)
    {
        var nextGraphNodeId = GetNextId(identityMaps.GraphNodeIdsByCanonicalId.Values);
        var syntheticNodeKindsByCanonicalId = GetSyntheticGraphNodeKinds(GetSyntheticNodeIds(edges));
        var graphNodes = projects
            .Select(
                project => new GraphNodeRecord
                {
                    Id = identityMaps.GraphNodeIdsByCanonicalId.GetValueOrDefault(project.Id),
                    CanonicalId = project.Id,
                    Kind = GraphNodeKind.Project
                })
            .Concat(
                codeNodes
                    .Select(
                        codeNode => new GraphNodeRecord
                        {
                            Id = identityMaps.GraphNodeIdsByCanonicalId.GetValueOrDefault(codeNode.CanonicalId),
                            CanonicalId = codeNode.CanonicalId,
                            Kind = GraphNodeKind.Code
                        }))
            .Concat(
                GetSyntheticNodeIds(edges)
                    .Select(
                        syntheticNodeId => new GraphNodeRecord
                        {
                            Id = identityMaps.GraphNodeIdsByCanonicalId.GetValueOrDefault(syntheticNodeId),
                            CanonicalId = syntheticNodeId,
                            Kind = syntheticNodeKindsByCanonicalId[syntheticNodeId]
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

    internal static string[] GetSyntheticNodeIds(IReadOnlyCollection<IndexedDependency> edges)
        => edges
            .Where(static edge =>
                HttpNodeIdentity.IsPlaceholderId(edge.CalleeId) ||
                PackageNodeIdentity.IsPlaceholderId(edge.CalleeId))
            .Select(static edge => edge.CalleeId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static edge => edge, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyDictionary<string, GraphNodeKind> GetSyntheticGraphNodeKinds(
        IEnumerable<string> syntheticNodeIds)
        => syntheticNodeIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static syntheticNodeId => syntheticNodeId, StringComparer.Ordinal)
            .ToDictionary(
                syntheticNodeId => syntheticNodeId,
                static syntheticNodeId => GetSyntheticGraphNodeKind(syntheticNodeId),
                StringComparer.Ordinal);

    private static GraphNodeKind GetSyntheticGraphNodeKind(
        string canonicalId)
    {
        if (HttpNodeIdentity.IsPlaceholderId(canonicalId))
        {
            return GraphNodeKind.Http;
        }

        if (PackageNodeIdentity.IsPlaceholderId(canonicalId))
        {
            return GraphNodeKind.Package;
        }

        throw new InvalidOperationException($"Unsupported synthetic node id '{canonicalId}'.");
    }

    private static int GetNextId(IEnumerable<int> ids)
        => ids.DefaultIfEmpty()
            .Max() + 1;

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

    internal sealed record PersistedIdentityMaps(
        IReadOnlyDictionary<string, int> DirectoryIdsByPath,
        IReadOnlyDictionary<string, int> DocumentIdsByRelativePath,
        IReadOnlyDictionary<string, int> GraphNodeIdsByCanonicalId);

    internal sealed record PersistedGraph(
        DirectoryRecord[] Directories,
        DirectoryClosureRecord[] DirectoryClosures,
        DocumentRecord[] Documents,
        GraphNodeRecord[] GraphNodes,
        ProjectNodeRecord[] ProjectNodes,
        CodeNodeRecord[] CodeNodes,
        DependencyEdgeRecord[] DependencyEdges);
}
