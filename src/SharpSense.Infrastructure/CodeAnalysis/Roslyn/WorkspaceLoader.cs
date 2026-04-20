using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed class WorkspaceLoader(IMsBuildWorkspaceFactory workspaceFactory)
{
    private readonly IMsBuildWorkspaceFactory _workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));

    public async Task<WorkspaceLoadResult> Load(
        string absoluteSolutionPath,
        ConcurrentQueue<string> diagnostics,
        RoslynWorkspaceOptions? options,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteSolutionPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var workspace = _workspaceFactory.Create(options);

        try
        {
            workspace.RegisterWorkspaceFailedHandler(
                args => diagnostics.Enqueue(args.Diagnostic.ToString()),
                null);
            using var openSolutionActivity = SharpSenseTraceSpan.Start("roslyn.open-solution");
            var solution = await workspace.OpenSolutionAsync(absoluteSolutionPath, cancellationToken: ct);
            var orderedProjects = solution.Projects
                .OrderBy(static project => project.FilePath ?? project.Name, StringComparer.Ordinal)
                .ToArray();

            openSolutionActivity.AddTag("solution.project.count", orderedProjects.Length);

            return new WorkspaceLoadResult(workspace, orderedProjects);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    public (IReadOnlyList<ProjectNode> Projects, IReadOnlyDictionary<ProjectId, string> ProjectIds) BuildProjectNodes(
        IReadOnlyList<Project> orderedProjects,
        IRepositoryWorkspace repositoryWorkspace)
    {
        ArgumentNullException.ThrowIfNull(orderedProjects);
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);

        using var projectNodeActivity = SharpSenseTraceSpan.Start("roslyn.build-project-nodes");
        var projects = new List<ProjectNode>(orderedProjects.Count);
        var projectIds = new Dictionary<ProjectId, string>();

        foreach (var project in orderedProjects)
        {
            var projectFilePath = RoslynPathUtilities.GetRequiredProjectFilePath(project);
            var relativeFilePath = repositoryWorkspace.ToRepositoryRelativePath(projectFilePath);
            var projectId = $"project:{relativeFilePath}";

            projectIds[project.Id] = projectId;
            projects.Add(
                new ProjectNode
                {
                    Id = projectId,
                    Name = project.Name,
                    RelativeFilePath = relativeFilePath,
                    ContentHash = RoslynPathUtilities.ComputeContentHash(projectFilePath)
                });
        }

        projectNodeActivity.AddTag("solution.project.count", projects.Count);

        return (projects, projectIds);
    }
}

internal sealed class WorkspaceLoadResult : IDisposable
{
    private readonly MSBuildWorkspace _workspace;

    public WorkspaceLoadResult(
        MSBuildWorkspace workspace,
        IReadOnlyList<Project> orderedProjects)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        OrderedProjects = orderedProjects ?? throw new ArgumentNullException(nameof(orderedProjects));
    }

    public IReadOnlyList<Project> OrderedProjects { get; }

    public void Dispose()
    {
        _workspace.Dispose();
    }
}
