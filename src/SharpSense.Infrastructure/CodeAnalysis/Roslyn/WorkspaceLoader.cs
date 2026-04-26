using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.MSBuild;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed class WorkspaceLoader(IMsBuildWorkspaceFactory workspaceFactory) : IDisposable
{
    private readonly IMsBuildWorkspaceFactory _workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));
    private readonly ConcurrentDictionary<string, WorkspaceSession> _activeWorkspaces = new(GetPathComparer());
    private bool _disposed;

    public async Task<WorkspaceLoadResult> Load(
        string absoluteTargetPath,
        ConcurrentQueue<string> diagnostics,
        RoslynWorkspaceOptions? options,
        CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteTargetPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var normalizedTargetPath = Path.GetFullPath(absoluteTargetPath);
        if (_activeWorkspaces.TryGetValue(normalizedTargetPath, out var activeWorkspace))
        {
            return await CreateLoadResult(activeWorkspace, ct);
        }

        var createdWorkspace = await OpenWorkspace(normalizedTargetPath, diagnostics, options, ct);
        var createdSession = new WorkspaceSession(createdWorkspace, createdWorkspace.CurrentSolution, options);
        var cachedWorkspace = _activeWorkspaces.GetOrAdd(normalizedTargetPath, createdSession);
        if (!ReferenceEquals(cachedWorkspace, createdSession))
        {
            createdSession.Dispose();
        }

        return await CreateLoadResult(cachedWorkspace, ct);
    }

    public async Task<Solution> UpdateDocuments(
        string absoluteTargetPath,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        ConcurrentQueue<string> diagnostics,
        CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteTargetPath);
        ArgumentNullException.ThrowIfNull(changedFiles);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var normalizedTargetPath = Path.GetFullPath(absoluteTargetPath);
        if (!_activeWorkspaces.TryGetValue(normalizedTargetPath, out var activeWorkspace))
        {
            throw new InvalidOperationException(
                $"Workspace '{normalizedTargetPath}' must be loaded before documents can be updated.");
        }

        await activeWorkspace.Gate.WaitAsync(ct);

        try
        {
            if (RequiresReload(changedFiles))
            {
                return await ReplaceWorkspace(normalizedTargetPath, activeWorkspace, diagnostics, ct);
            }

            var updatedSolution = activeWorkspace.ActiveSolution;

            foreach (var changedFile in changedFiles)
            {
                var currentPath = changedFile.GetCurrentPath();
                if (string.IsNullOrWhiteSpace(currentPath))
                {
                    continue;
                }

                var absoluteFilePath = Path.GetFullPath(currentPath);
                if (!File.Exists(absoluteFilePath))
                {
                    return await ReplaceWorkspace(normalizedTargetPath, activeWorkspace, diagnostics, ct);
                }

                var documentIds = FindDocumentIds(updatedSolution, absoluteFilePath);
                if (documentIds.Count == 0)
                {
                    return await ReplaceWorkspace(normalizedTargetPath, activeWorkspace, diagnostics, ct);
                }

                var documentText = SourceText.From(await File.ReadAllTextAsync(absoluteFilePath, ct));
                foreach (var documentId in documentIds)
                {
                    updatedSolution = updatedSolution.WithDocumentText(
                        documentId,
                        documentText,
                        PreservationMode.PreserveIdentity);
                }
            }

            activeWorkspace.UpdateActiveSolution(updatedSolution);
            return updatedSolution;
        }
        finally
        {
            activeWorkspace.Gate.Release();
        }
    }

    public IReadOnlyDictionary<ProjectId, string> BuildProjectIds(
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

    public (IReadOnlyList<ProjectNode> Projects, IReadOnlyDictionary<ProjectId, string> ProjectIds) BuildProjectNodes(
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
                    ContentHash = RoslynPathUtilities.ComputeContentHash(projectFilePath)
                });
        }

        projectNodeActivity.AddTag("target.project.count", projects.Count);

        return (projects, projectIds);
    }

    private async Task<MSBuildWorkspace> OpenWorkspace(
        string absoluteTargetPath,
        ConcurrentQueue<string> diagnostics,
        RoslynWorkspaceOptions? options,
        CancellationToken ct)
    {
        var workspace = _workspaceFactory.Create(options);

        try
        {
            workspace.RegisterWorkspaceFailedHandler(
                args => diagnostics.Enqueue(args.Diagnostic.ToString()),
                null);
            using var openTargetActivity = SharpSenseTraceSpan.Start("roslyn.open-target");
            if (IsSolutionTargetPath(absoluteTargetPath))
            {
                var solution = await workspace.OpenSolutionAsync(absoluteTargetPath, cancellationToken: ct);
                openTargetActivity.AddTag("target.project.count", solution.Projects.Count());
            }
            else
            {
                var project = await workspace.OpenProjectAsync(absoluteTargetPath, cancellationToken: ct);
                openTargetActivity.AddTag("target.project.count", project.Solution.Projects.Count());
            }

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private async Task<Solution> ReplaceWorkspace(
        string absoluteTargetPath,
        WorkspaceSession activeWorkspace,
        ConcurrentQueue<string> diagnostics,
        CancellationToken ct)
    {
        var replacementWorkspace = await OpenWorkspace(
            absoluteTargetPath,
            diagnostics,
            activeWorkspace.Options,
            ct);

        activeWorkspace.Replace(replacementWorkspace, replacementWorkspace.CurrentSolution);
        return activeWorkspace.ActiveSolution;
    }

    private static IReadOnlyList<DocumentId> FindDocumentIds(Solution solution, string absoluteFilePath)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);

        var pathComparer = GetPathComparer();

        return
        [
            .. solution.Projects
                .SelectMany(static project => project.Documents)
                .Where(document => !string.IsNullOrWhiteSpace(document.FilePath) &&
                                   pathComparer.Equals(Path.GetFullPath(document.FilePath), absoluteFilePath))
                .Select(static document => document.Id)
        ];
    }

    private static bool RequiresReload(IReadOnlyList<WorkspaceFileChange> changedFiles)
        => changedFiles.Any(changedFile => changedFile.ActionType is not WorkspaceFileChangeAction.Modified);

    private static bool IsSolutionTargetPath(string absoluteTargetPath)
        => string.Equals(Path.GetExtension(absoluteTargetPath), ".sln", StringComparison.OrdinalIgnoreCase);

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static async Task<WorkspaceLoadResult> CreateLoadResult(
        WorkspaceSession workspaceSession,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workspaceSession);

        await workspaceSession.Gate.WaitAsync(ct);

        try
        {
            return new WorkspaceLoadResult(workspaceSession.ActiveSolution);
        }
        finally
        {
            workspaceSession.Gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var targetPath in _activeWorkspaces.Keys)
        {
            if (_activeWorkspaces.TryRemove(targetPath, out var workspaceSession))
            {
                workspaceSession.Dispose();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WorkspaceLoader));
        }
    }
}

internal sealed class WorkspaceLoadResult
{
    public WorkspaceLoadResult(Solution solution)
    {
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
    }

    public Solution Solution { get; }

    public IReadOnlyList<Project> OrderedProjects =>
        [
            .. Solution.Projects
                .OrderBy(static project => project.FilePath ?? project.Name, StringComparer.Ordinal)
        ];
}

internal sealed class WorkspaceSession(
    MSBuildWorkspace workspace,
    Solution activeSolution,
    RoslynWorkspaceOptions? options) : IDisposable
{
    public MSBuildWorkspace Workspace { get; private set; } = workspace ?? throw new ArgumentNullException(nameof(workspace));

    public Solution ActiveSolution { get; private set; } = activeSolution ?? throw new ArgumentNullException(nameof(activeSolution));

    public RoslynWorkspaceOptions? Options { get; } = options;

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public void UpdateActiveSolution(Solution activeSolution)
        => ActiveSolution = activeSolution ?? throw new ArgumentNullException(nameof(activeSolution));

    public void Replace(
        MSBuildWorkspace workspace,
        Solution activeSolution)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(activeSolution);

        var previousWorkspace = Workspace;
        Workspace = workspace;
        ActiveSolution = activeSolution;
        previousWorkspace.Dispose();
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Gate.Dispose();
    }
}
