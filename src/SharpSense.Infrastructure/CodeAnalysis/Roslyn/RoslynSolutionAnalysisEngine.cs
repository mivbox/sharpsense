using System.Collections.Concurrent;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

public sealed class RoslynSolutionAnalysisEngine : IRoslynSolutionAnalysisEngine
{
    private readonly WorkspaceLoader _workspaceLoader;
    private readonly NodeExtractor _nodeExtractor;
    private readonly EdgeExtractor _edgeExtractor;

    public RoslynSolutionAnalysisEngine(IMsBuildWorkspaceFactory? workspaceFactory = null)
        : this(
            new WorkspaceLoader(workspaceFactory ?? new MsBuildWorkspaceFactory()),
            new NodeExtractor(),
            new EdgeExtractor())
    {
    }

    internal RoslynSolutionAnalysisEngine(
        WorkspaceLoader workspaceLoader,
        NodeExtractor nodeExtractor,
        EdgeExtractor edgeExtractor)
    {
        _workspaceLoader = workspaceLoader ?? throw new ArgumentNullException(nameof(workspaceLoader));
        _nodeExtractor = nodeExtractor ?? throw new ArgumentNullException(nameof(nodeExtractor));
        _edgeExtractor = edgeExtractor ?? throw new ArgumentNullException(nameof(edgeExtractor));
    }

    public async Task<KnowledgeGraphExtractionPayload> Extract(
        string solutionPath,
        IRepositoryWorkspace repositoryWorkspace,
        RoslynWorkspaceOptions? options = null,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);

        var absoluteSolutionPath = Path.GetFullPath(solutionPath);
        var diagnostics = new ConcurrentQueue<string>();

        using var activity = SharpSenseTraceSpan.Start("roslyn.extract");
        activity.AddTag("solution.path", absoluteSolutionPath);
        activity.AddTag("repository.root", repositoryWorkspace.RootPath);

        try
        {
            using var loadedWorkspace = await _workspaceLoader.Load(absoluteSolutionPath, diagnostics, options, ct);
            activity.AddTag("solution.project.count", loadedWorkspace.OrderedProjects.Count);

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
                loadedWorkspace.OrderedProjects,
                projectIds,
                nodeExtraction.DeclaredSymbols,
                nodeExtraction.SymbolNodeIds);

            activity.AddTag("index.code_node.count", nodeExtraction.CodeNodes.Count);
            activity.AddTag("index.dependency.count", edges.Count);

            return new KnowledgeGraphExtractionPayload(
                absoluteSolutionPath,
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
}
