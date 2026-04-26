using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

public sealed class RoslynTargetAnalysisEngine : IRoslynTargetAnalysisEngine, IDisposable
{
    private readonly WorkspaceLoader _workspaceLoader;
    private readonly NodeExtractor _nodeExtractor;
    private readonly EdgeExtractor _edgeExtractor;
    private bool _disposed;

    public RoslynTargetAnalysisEngine(IMsBuildWorkspaceFactory? workspaceFactory = null)
        : this(
            new WorkspaceLoader(workspaceFactory ?? new MsBuildWorkspaceFactory()),
            new NodeExtractor(),
            new EdgeExtractor())
    {
    }

    internal RoslynTargetAnalysisEngine(
        WorkspaceLoader workspaceLoader,
        NodeExtractor nodeExtractor,
        EdgeExtractor edgeExtractor)
    {
        _workspaceLoader = workspaceLoader ?? throw new ArgumentNullException(nameof(workspaceLoader));
        _nodeExtractor = nodeExtractor ?? throw new ArgumentNullException(nameof(nodeExtractor));
        _edgeExtractor = edgeExtractor ?? throw new ArgumentNullException(nameof(edgeExtractor));
    }

    public async Task<KnowledgeGraphExtractionPayload> Extract(
        string targetPath,
        IRepositoryWorkspace repositoryWorkspace,
        RoslynWorkspaceOptions? options = null,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);

        var absoluteTargetPath = Path.GetFullPath(targetPath);
        var diagnostics = new ConcurrentQueue<string>();

        using var activity = SharpSenseTraceSpan.Start("roslyn.extract");
        activity.AddTag("target.path", absoluteTargetPath);
        activity.AddTag("repository.root", repositoryWorkspace.RootPath);

        try
        {
            var loadedWorkspace = await _workspaceLoader.Load(absoluteTargetPath, diagnostics, options, ct);
            activity.AddTag("target.project.count", loadedWorkspace.OrderedProjects.Count);

            var (projects, projectIds) = _workspaceLoader.BuildProjectNodes(
                loadedWorkspace.OrderedProjects,
                repositoryWorkspace);
            var nodeExtraction = await _nodeExtractor.Extract(
                loadedWorkspace.OrderedProjects,
                repositoryWorkspace,
                projectIds,
                diagnostics,
                progress,
                ct);
            var edges = _edgeExtractor.Extract(
                loadedWorkspace.Solution,
                loadedWorkspace.OrderedProjects,
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
                diagnostics.ToArray());
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    public async Task<KnowledgeGraphExtractionPayload> ExtractIncremental(
        string targetPath,
        IRepositoryWorkspace repositoryWorkspace,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);
        ArgumentNullException.ThrowIfNull(changedFiles);

        var absoluteTargetPath = Path.GetFullPath(targetPath);
        var absoluteChanges = NormalizeChanges(changedFiles, repositoryWorkspace.RootPath);
        var diagnostics = new ConcurrentQueue<string>();

        using var activity = SharpSenseTraceSpan.Start("roslyn.extract.incremental");
        activity.AddTag("target.path", absoluteTargetPath);
        activity.AddTag("repository.root", repositoryWorkspace.RootPath);
        activity.AddTag("target.change.count", absoluteChanges.Count);

        try
        {
            await _workspaceLoader.Load(absoluteTargetPath, diagnostics, new RoslynWorkspaceOptions(), ct);
            var updatedSolution = await _workspaceLoader.UpdateDocuments(absoluteTargetPath, absoluteChanges, diagnostics, ct);
            var changedDocuments = GetChangedDocuments(updatedSolution, absoluteChanges);
            if (changedDocuments.Count == 0)
            {
                return new KnowledgeGraphExtractionPayload(
                    absoluteTargetPath,
                    [],
                    [],
                    [],
                    diagnostics.ToArray());
            }

            var projectIds = _workspaceLoader.BuildProjectIds(OrderProjects(updatedSolution), repositoryWorkspace);
            var nodeExtraction = await _nodeExtractor.ExtractDocuments(
                changedDocuments,
                repositoryWorkspace,
                projectIds,
                diagnostics,
                progress,
                ct);
            var edges = _edgeExtractor.ExtractIncremental(
                updatedSolution,
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
                diagnostics.ToArray());
        }
        catch (Exception ex)
        {
            activity.RecordExceptionAndErrorStatus(ex);
            throw;
        }
    }

    private static IReadOnlyList<WorkspaceFileChange> NormalizeChanges(
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

    private static IReadOnlyList<Document> GetChangedDocuments(
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

            currentPaths.Add(Path.GetFullPath(currentPath));
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
                                   currentPaths.Contains(Path.GetFullPath(document.FilePath)))
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

    private static string? NormalizePath(string? path, string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(repositoryRoot, path));
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workspaceLoader.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RoslynTargetAnalysisEngine));
        }
    }
}
