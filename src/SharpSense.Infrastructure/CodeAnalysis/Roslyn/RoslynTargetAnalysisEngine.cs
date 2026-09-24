using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Storage;
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

    public Task<KnowledgeGraphExtractionPayload> Extract(
        string targetPath,
        Project project,
        IRepositoryWorkspace repositoryWorkspace,
        IProgress<IndexingProgress>? progress = null,
        IReadOnlyCollection<string>? diagnostics = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        return Extract(
            targetPath,
            project.Solution,
            repositoryWorkspace,
            progress,
            diagnostics,
            ct);
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
                nodeExtraction.DeclaredSymbols,
                nodeExtraction.SymbolNodeIds);

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

    public async Task<KnowledgeGraphExtractionPayload> ExtractIncremental(
        string targetPath,
        Solution solution,
        IRepositoryWorkspace repositoryWorkspace,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        IProgress<IndexingProgress>? progress = null,
        IReadOnlyCollection<string>? diagnostics = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);
        ArgumentNullException.ThrowIfNull(changedFiles);

        var absoluteTargetPath = _fileSystem.Path.GetFullPath(targetPath);
        var absoluteChanges = NormalizeChanges(changedFiles, repositoryWorkspace.RootPath);
        var diagnosticQueue = CreateDiagnostics(diagnostics);

        using var activity = SharpSenseTraceSpan.Start("roslyn.extract.incremental");
        activity.AddTag("target.path", absoluteTargetPath);
        activity.AddTag("repository.root", repositoryWorkspace.RootPath);
        activity.AddTag("target.change.count", absoluteChanges.Count);

        try
        {
            var expandedChanges = await PartialDeclarationChanges.Expand(solution, absoluteChanges, ct);
            var changedDocuments = GetChangedDocuments(solution, expandedChanges);
            if (changedDocuments.Count == 0)
            {
                return new KnowledgeGraphExtractionPayload(
                    absoluteTargetPath,
                    [],
                    [],
                    [],
                    diagnosticQueue.ToArray());
            }

            var projectIds = BuildProjectIds(OrderProjects(solution), repositoryWorkspace);
            var nodeExtraction = await _nodeExtractor.ExtractDocuments(
                changedDocuments,
                repositoryWorkspace,
                projectIds,
                diagnosticQueue,
                progress,
                ct);
            var edges = _edgeExtractor.ExtractIncremental(
                solution,
                projectIds,
                nodeExtraction.DeclaredSymbols,
                nodeExtraction.SymbolNodeIds);

            activity.AddTag("index.code_node.count", nodeExtraction.CodeNodes.Count);
            activity.AddTag("index.dependency.count", edges.Count);

            return new KnowledgeGraphExtractionPayload(
                absoluteTargetPath,
                [],
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
        var projectIds = BuildProjectIds(orderedProjects, repositoryWorkspace);

        foreach (var project in orderedProjects)
        {
            var projectFilePath = RoslynPathUtilities.GetRequiredProjectFilePath(project);
            var relativeFilePath = repositoryWorkspace.ToRepositoryRelativePath(projectFilePath);
            var projectId = projectIds[project.Id];
            projects.Add(
                new ProjectNode
                {
                    Id = projectId,
                    Name = project.Name,
                    RelativeFilePath = relativeFilePath,
                    ContentHash = RoslynPathUtilities.ComputeContentHash(_fileSystem, projectFilePath)
                });
        }

        projectNodeActivity.AddTag("target.project.count", projects.Count);

        return (projects, projectIds);
    }

    private IReadOnlyDictionary<ProjectId, string> BuildProjectIds(
        IReadOnlyList<Project> orderedProjects,
        IRepositoryWorkspace repositoryWorkspace)
    {
        ArgumentNullException.ThrowIfNull(orderedProjects);
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);

        var projectIds = new Dictionary<ProjectId, string>();

        foreach (var project in orderedProjects)
        {
            var projectFilePath = RoslynPathUtilities.GetRequiredProjectFilePath(project);
            var relativeFilePath = repositoryWorkspace.ToRepositoryRelativePath(projectFilePath);
            projectIds[project.Id] = $"project:{relativeFilePath}";
        }

        return projectIds;
    }

    private IReadOnlyList<WorkspaceFileChange> NormalizeChanges(
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        string repositoryRoot)
    {
        return
        [
            .. changedFiles.Select(
                change => change with
                {
                    OldPath = NormalizePath(change.OldPath, repositoryRoot),
                    NewPath = NormalizePath(change.NewPath, repositoryRoot)
                })
        ];
    }

    private IReadOnlyList<Document> GetChangedDocuments(
        Solution solution,
        IReadOnlyList<WorkspaceFileChange> changedFiles)
    {
        var currentPaths = new HashSet<string>(GetPathComparer());

        foreach (var changedFile in changedFiles)
        {
            var currentPath = changedFile.GetCurrentPath();
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                continue;
            }

            currentPaths.Add(_fileSystem.Path.GetFullPath(currentPath));
        }

        if (currentPaths.Count == 0)
        {
            return [];
        }

        return
        [
            .. solution.Projects
                .SelectMany(static project => project.Documents)
                .Where(document => !string.IsNullOrWhiteSpace(document.FilePath) &&
                                   currentPaths.Contains(_fileSystem.Path.GetFullPath(document.FilePath)))
                .OrderBy(static document => document.FilePath ?? document.Name, StringComparer.Ordinal)
        ];
    }

    private static IReadOnlyList<Project> OrderProjects(Solution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        return
        [
            .. solution.Projects
                .OrderBy(static project => project.FilePath ?? project.Name, StringComparer.Ordinal)
        ];
    }

    private string? NormalizePath(string? path, string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return _fileSystem.Path.GetFullPath(
            _fileSystem.Path.IsPathRooted(path)
                ? path
                : _fileSystem.Path.Combine(repositoryRoot, path));
    }

    private static ConcurrentQueue<string> CreateDiagnostics(IReadOnlyCollection<string>? diagnostics)
        => diagnostics is null ? new ConcurrentQueue<string>() : new ConcurrentQueue<string>(diagnostics);

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
