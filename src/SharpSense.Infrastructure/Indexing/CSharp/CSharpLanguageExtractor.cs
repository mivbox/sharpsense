using FluentResults;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing.CSharp;

internal sealed class CSharpLanguageExtractor(
    IWorkspaceLoader workspaceLoader,
    ITargetAnalysisEngine analysisEngine,
    IRepositoryWorkspace repositoryWorkspace,
    ICSharpWorkspaceTargetResolver workspaceTargetResolver)
    : ILanguageExtractor
{
    public WorkspaceSourceKind SourceKind => WorkspaceSourceKind.CSharp;

    public async Task<Result<ExtractedNodes>> Extract(
        ExtractionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TargetPath);

        var workspaceTargetPath = workspaceTargetResolver.ResolveTargetPath(context.TargetPath);
        if (workspaceTargetPath is null)
        {
            context.Progress?.Report(
                new IndexingProgress(
                    "No C# solution/project found",
                    1,
                    1));

            return Result.Ok(new ExtractedNodes([], [], [], []));
        }

        var loadedWorkspace = await RefreshWorkspace(context, workspaceTargetPath, ct);

        if (loadedWorkspace.IsFailed)
        {
            return Result.Fail(loadedWorkspace.Errors);
        }

        var extractionPayload = await analysisEngine.Extract(
            workspaceTargetPath,
            loadedWorkspace.Value.Solution,
            repositoryWorkspace,
            context.Progress,
            loadedWorkspace.Value.Diagnostics,
            ct);

        return Result.Ok(new ExtractedNodes(
            [.. extractionPayload.Projects.Select(ToIndexedProject)],
            [.. extractionPayload.CodeNodes.Select(ToIndexedCodeNode)],
            [.. extractionPayload.Edges.Select(ToIndexedDependency)],
            extractionPayload.Diagnostics,
            [
                .. loadedWorkspace.Value.Solution.Projects
                    .SelectMany(static project => project.Documents
                        .Select(static document => document.FilePath)
                        .Concat(project.AdditionalDocuments.Select(static document => document.FilePath))
                        .Concat(project.AnalyzerConfigDocuments.Select(static document => document.FilePath)))
                    .OfType<string>()
                    .Distinct(FileSystemPaths.Comparer)
            ],
            CanReuseForDocumentationChanges: true));
    }

    private async Task<Result<WorkspaceLoadResult>> RefreshWorkspace(
        ExtractionContext context,
        string workspaceTargetPath,
        CancellationToken ct)
    {
        using var activity = SharpSenseTraceSpan.Start("roslyn.workspace.refresh");
        var sourceOnlyChanges = context.ChangedFiles is { Count: > 0 } &&
            context.ChangedFiles.All(static change =>
                change.ActionType == WorkspaceFileChangeAction.Modified &&
                change.GetCurrentPath() is { } path && IsCSharpFilePath(path));

        activity.AddTag("workspace.refresh.mode", sourceOnlyChanges ? "source" : "reload");
        if (!sourceOnlyChanges)
        {
            return await workspaceLoader.Reload(workspaceTargetPath, ct);
        }

        var loadedWorkspace = await workspaceLoader.Load(workspaceTargetPath, ct);
        if (loadedWorkspace.IsFailed)
        {
            return loadedWorkspace;
        }

        if (loadedWorkspace.Value.Solution.Projects
            .Any(static project => project.AdditionalDocuments.Any()))
        {
            // Generators may depend on arbitrary AdditionalFiles that are not source-file
            // events. Reload these workspaces so the next source edit observes those inputs.
            activity.AddTag("workspace.refresh.fallback", "additional-files");

            return await workspaceLoader.Reload(workspaceTargetPath, ct);
        }

        // Existing source edits preserve MSBuild evaluation and Roslyn's compilation caches.
        // Still extract the complete solution: partial declarations, dependent signatures,
        // and generated code can change outside the edited documents.
        var changes = context.ChangedFiles!
            .Select(change => change with
            {
                OldPath = NormalizePath(change.OldPath),
                NewPath = NormalizePath(change.NewPath)
            })
            .ToArray();
        var updatedWorkspace = await workspaceLoader.UpdateDocuments(workspaceTargetPath, changes, ct);
        if (updatedWorkspace.IsFailed)
        {
            return updatedWorkspace;
        }

        return Result.Ok(new WorkspaceLoadResult(
            updatedWorkspace.Value.Solution,
            [.. loadedWorkspace.Value.Diagnostics, .. updatedWorkspace.Value.Diagnostics]));
    }

    private string? NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(repositoryWorkspace.ToRepositoryRelativePath(path), repositoryWorkspace.RootPath);

    private static IndexedProject ToIndexedProject(ProjectNode projectNode)
        => new(
            projectNode.Id,
            projectNode.Name,
            projectNode.RelativeFilePath,
            projectNode.ContentHash);

    private static IndexedCodeNode ToIndexedCodeNode(CodeNode codeNode)
        => new(
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
            codeNode.VectorEmbedding);

    private static IndexedDependency ToIndexedDependency(DependencyEdge dependencyEdge)
        => new(
            dependencyEdge.CallerId,
            dependencyEdge.CalleeId,
            dependencyEdge.EdgeType);

    private static bool IsCSharpFilePath(string path)
        => string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);
}
