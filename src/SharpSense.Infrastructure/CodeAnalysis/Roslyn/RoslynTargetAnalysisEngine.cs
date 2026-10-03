using Microsoft.CodeAnalysis;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Storage;
using System.Collections.Concurrent;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed class RoslynTargetAnalysisEngine : ITargetAnalysisEngine
{
    private readonly NodeExtractor _nodeExtractor;
    private readonly EdgeExtractor _edgeExtractor;
    private readonly IFileSystem _fileSystem;

    public RoslynTargetAnalysisEngine(
        NodeExtractor nodeExtractor,
        EdgeExtractor edgeExtractor,
        IFileSystem fileSystem)
    {
        _nodeExtractor = nodeExtractor ?? throw new ArgumentNullException(nameof(nodeExtractor));
        _edgeExtractor = edgeExtractor ?? throw new ArgumentNullException(nameof(edgeExtractor));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<KnowledgeGraphExtractionPayload> Extract(
        string targetPath,
        Solution solution,
        IRepositoryWorkspace repositoryWorkspace,
        IProgress<IndexingProgress>? progress = null,
        IReadOnlyCollection<string>? diagnostics = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);

        var absoluteTargetPath = _fileSystem.Path.GetFullPath(targetPath);
        var diagnosticQueue = CreateDiagnostics(diagnostics);
        var orderedProjects = OrderProjects(solution);

        using var activity = SharpSenseTraceSpan.Start("roslyn.extract");
        activity.AddTag("target.path", absoluteTargetPath);
        activity.AddTag("repository.root", repositoryWorkspace.RootPath);

        try
        {
            activity.AddTag("target.project.count", orderedProjects.Count);

            var (projects, projectIds) = BuildProjectNodes(
                orderedProjects,
                repositoryWorkspace);
            var nodeExtraction = await _nodeExtractor.Extract(
                orderedProjects,
                repositoryWorkspace,
                projectIds,
                diagnosticQueue,
                progress,
                ct);
            var edges = _edgeExtractor.Extract(
                solution,
                orderedProjects,
                projectIds,
                nodeExtraction.DeclaredSymbols);

            activity.AddTag("index.code_node.count", nodeExtraction.CodeNodes.Count);
            activity.AddTag("index.dependency.count", edges.Count);

            return new KnowledgeGraphExtractionPayload(
                absoluteTargetPath,
                projects,
                nodeExtraction.CodeNodes,
                edges,
                diagnosticQueue.ToArray());
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    private (IReadOnlyList<ProjectNode> Projects, IReadOnlyDictionary<ProjectId, string> ProjectIds) BuildProjectNodes(
        IReadOnlyList<Project> orderedProjects,
        IRepositoryWorkspace repositoryWorkspace)
    {
        ArgumentNullException.ThrowIfNull(orderedProjects);
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);

        using var projectNodeActivity = SharpSenseTraceSpan.Start("roslyn.build-project-nodes");
        var projects = new List<ProjectNode>(orderedProjects.Count);
        var projectIds = new Dictionary<ProjectId, string>();

        foreach (var projectGroup in orderedProjects.GroupBy(
            RoslynPathUtilities.GetRequiredProjectFilePath,
            FileSystemPaths.Comparer))
        {
            var relativeFilePath = repositoryWorkspace.ToRepositoryRelativePath(projectGroup.Key);
            var contentHash = RoslynPathUtilities.ComputeContentHash(_fileSystem, projectGroup.Key);
            var hasMultipleTargets = projectGroup.Skip(1).Any();

            foreach (var project in projectGroup)
            {
                // Roslyn includes the target framework in each multi-target project name.
                // Keep existing single-target identities stable across reindexing.
                var targetSuffix = hasMultipleTargets ? $"#{Uri.EscapeDataString(project.Name)}" : string.Empty;
                var projectId = $"project:{relativeFilePath}{targetSuffix}";
                projectIds[project.Id] = projectId;
                projects.Add(
                    new ProjectNode
                    {
                        Id = projectId,
                        Name = project.Name,
                        RelativeFilePath = relativeFilePath,
                        ContentHash = contentHash
                    });
            }
        }

        projectNodeActivity.AddTag("target.project.count", projects.Count);

        return (projects, projectIds);
    }

    private static IReadOnlyList<Project> OrderProjects(Solution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        return
        [
            .. solution.Projects.OrderBy(static project => project.FilePath ?? project.Name, StringComparer.Ordinal)
        ];
    }

    private static ConcurrentQueue<string> CreateDiagnostics(IReadOnlyCollection<string>? diagnostics)
        => diagnostics is null ? new ConcurrentQueue<string>() : new ConcurrentQueue<string>(diagnostics);
}
