using FluentResults;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using SharpSense.Application.GraphStats.Abstractions;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Errors;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;

namespace SharpSense.Application.Indexing.UpdateWorkspaceFiles;

public sealed class UpdateWorkspaceFilesCommandHandler(
    IEnumerable<ILanguageExtractor> extractors,
    IEmbeddingGenerator embeddingGenerator,
    IKnowledgeGraphRepository knowledgeGraphRepository,
    IIndexingWorkspacePaths workspacePaths,
    IWorkspaceFileDiscoverer workspaceFileDiscoverer,
    IOptions<SharpSenseCliOptions> cliOptions,
    IIndexRunStore? indexRunStore = null,
    ILogger<UpdateWorkspaceFilesCommandHandler>? logger = null,
    ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>? workspaceIndexer = null,
    IWorkspaceChangeFilter? workspaceChangeFilter = null)
    : ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>
{
    private static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdown", ".mkd"];
    private static readonly string[] IncrementalDiscoveryGlobs = ["**/*.cs", .. TypeScriptIndexingPathRules.IncludeGlobs, "**/*.md", "**/*.markdown", "**/*.mdown", "**/*.mkd"];
    private readonly SharpSenseCliOptions _cliOptions = cliOptions?.Value ?? throw new ArgumentNullException(nameof(cliOptions));

    public async Task<Result<UpdateWorkspaceFilesOutcome>> Handle(UpdateWorkspaceFilesCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.ChangedFiles);

        if (_cliOptions.WorkspaceSources.Count > 0 || !string.IsNullOrWhiteSpace(_cliOptions.WorkspaceId))
        {
            return await UpdateNamedWorkspace(command, ct);
        }

        var absoluteTargetPath = workspacePaths.GetRequiredTargetPath(GetRequiredTargetPath());

        using var trace = SharpSenseTraceSpan.Start("index.target.incremental");
        trace.AddTag("target.path", absoluteTargetPath);
        trace.AddTag("repository.root", workspacePaths.RootPath);
        trace.AddTag("index.change.count", command.ChangedFiles.Count);
        await using var run = new IndexingRunDiagnostics(indexRunStore, "incremental", absoluteTargetPath, logger);

        try
        {
            IReadOnlyList<WorkspaceFileChange> expandedChangedFiles;
            using (run.Measure("discovery"))
            {
                expandedChangedFiles = await ExpandDirectoryChanges(command.ChangedFiles, ct);
            }
            var changedFilePaths = GetAffectedRelativePaths(expandedChangedFiles);
            var refreshWorkspace = CSharpIndexingPathRules.IsWorkspaceTarget(absoluteTargetPath) &&
                RequiresCSharpRefresh(expandedChangedFiles);
            var refreshTypeScript = !refreshWorkspace && RequiresTypeScriptRefresh(expandedChangedFiles);
            trace.AddTag("index.expanded_change.count", expandedChangedFiles.Count);

            if (expandedChangedFiles.Count == 0 || (changedFilePaths.Length == 0 && !refreshTypeScript && !refreshWorkspace))
            {
                return Result.Ok(new UpdateWorkspaceFilesOutcome(0, 0, 0, IndexCommitted: false));
            }

            if (refreshTypeScript)
            {
                // TypeScript imports, aliases, and shared synthetic dependencies can change
                // consumers outside the changed file. Replace the previous complete TS scope,
                // including files now deleted, excluded, or no longer reachable.
                var persistedPaths = await knowledgeGraphRepository.GetPersistedDocumentPathsUnderDirectory(string.Empty, ct);
                changedFilePaths = changedFilePaths
                    .Concat(persistedPaths.Where(TypeScriptIndexingPathRules.IsTypeScriptFilePath))
                    .Distinct(GetPathComparer())
                    .OrderBy(static path => path, GetPathComparer())
                    .ToArray();
            }

            using var extractActivity = SharpSenseTraceSpan.Start("index.extract.incremental");
            Result<ExtractedNodes> extractionResult;
            using (run.Measure("extraction"))
            {
                extractionResult = await Extract(
                    new IncrementalExtractionContext(absoluteTargetPath, expandedChangedFiles, command.Progress),
                    extractActivity,
                    refreshWorkspace,
                    ct);
            }

            if (extractionResult.IsFailed)
            {
                ct.ThrowIfCancellationRequested();
                trace.SetError();
                run.Failed(extractionResult.Errors);
                return Result.Fail(extractionResult.Errors);
            }

            var extractedNodes = extractionResult.Value;
            run.Extracted(extractedNodes);
            extractedNodes = NormalizePersistedPaths(extractedNodes);
            // Extractors may expand a partial declaration into its complete sibling documents.
            // Include those paths when loading cached vectors as well as when replacing graph data.
            changedFilePaths = changedFilePaths
                .Concat(extractedNodes.CodeNodes.Select(static node => node.RelativeFilePath))
                .Distinct(GetPathComparer())
                .OrderBy(static path => path, GetPathComparer())
                .ToArray();
            if (extractedNodes.CodeNodes.Count > 0)
            {
                using var embeddingTiming = run.Measure("embeddings");
                if (!_cliOptions.SkipEmbeddings)
                {
                    command.Progress?.Report(new IndexingProgress("Embedding phase...", changedFilePaths.Length, changedFilePaths.Length));
                }

                var persistedCodeNodes = _cliOptions.DisableEmbeddingCache
                    ? []
                    : refreshWorkspace
                        ? await knowledgeGraphRepository.GetPersistedCodeNodes(ct)
                        : await knowledgeGraphRepository.GetPersistedCodeNodes(changedFilePaths, ct);
                extractedNodes = extractedNodes with
                {
                    CodeNodes = await CodeNodeEmbeddingCoordinator.Populate(
                        extractedNodes.CodeNodes,
                        persistedCodeNodes,
                        _cliOptions.SkipEmbeddings,
                        _cliOptions.DisableEmbeddingCache,
                        embeddingGenerator,
                        progress: null,
                        ct,
                        run.Embeddings)
                };
            }

            command.Progress?.Report(new IndexingProgress("Persisting incremental index...", changedFilePaths.Length, changedFilePaths.Length));

            using (run.Measure("persistence"))
            {
                if (refreshWorkspace)
                {
                    // Project membership and semantic dependencies may change in untouched files.
                    // Commit one complete snapshot so removed projects and their consumers reconcile together.
                    await knowledgeGraphRepository.ReplaceTarget(extractedNodes, ct);
                }
                else
                {
                    await knowledgeGraphRepository.ReplaceWorkspaceFiles(changedFilePaths, extractedNodes, ct);
                }
            }
            run.Succeeded();

            trace.AddTag("index.code_node.count", extractedNodes.CodeNodes.Count);
            trace.AddTag("index.dependency.count", extractedNodes.Edges.Count);
            trace.AddTag("index.diagnostic.count", extractedNodes.Diagnostics.Count);

            return Result.Ok(new UpdateWorkspaceFilesOutcome(
                ProjectsReindexed: extractedNodes.Projects.Count,
                CodeNodesPersisted: extractedNodes.CodeNodes.Count,
                DependencyEdgesPersisted: extractedNodes.Edges.Count));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            trace.SetError();
            run.Cancelled();
            return Result.Fail(new ServiceError(
                ServiceErrorCode.FailedPrecondition,
                $"Incremental update of '{absoluteTargetPath}' was cancelled."));
        }
        catch (Exception exception)
        {
            trace.RecordExceptionAndErrorStatus(exception);
            run.Failed(exception);
            return Result.Fail(new ServiceError(
                ServiceErrorCode.InternalError,
                $"Incremental update of '{absoluteTargetPath}' failed: {exception.Message}"));
        }
    }

    private async Task<Result<UpdateWorkspaceFilesOutcome>> UpdateNamedWorkspace(
        UpdateWorkspaceFilesCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command.ChangedFiles);

        if (command.ChangedFiles.Count == 0 || workspaceChangeFilter?.IsRelevant(command.ChangedFiles) == false)
        {
            return Result.Ok(new UpdateWorkspaceFilesOutcome(0, 0, 0, IndexCommitted: false));
        }

        if (workspaceIndexer is null)
        {
            return Result.Fail(new ServiceError(
                ServiceErrorCode.FailedPrecondition,
                "Workspace indexing handler is not registered. Register the complete indexing feature."));
        }

        // Reconcile the complete selected graph. A change in one contribution must not
        // replace another source or leave reverse links stale. C# source-only edits can
        // still reuse their loaded Roslyn workspaces through ChangedFiles.
        var result = await workspaceIndexer.Handle(new IndexTargetCommand(
            Progress: command.Progress,
            ChangedFiles: command.ChangedFiles), ct);
        return result.IsFailed
            ? Result.Fail(result.Errors)
            : Result.Ok(new UpdateWorkspaceFilesOutcome(
                result.Value.ProjectsIndexed,
                result.Value.CodeNodesPersisted,
                result.Value.DependencyEdgesPersisted));
    }

    private async Task<Result<ExtractedNodes>> Extract(
        IncrementalExtractionContext context,
        SharpSenseTraceSpan extractActivity,
        bool refreshWorkspace,
        CancellationToken ct)
    {
        var aggregatedProjects = new List<IndexedProject>();
        var aggregatedCodeNodes = new List<IndexedCodeNode>();
        var aggregatedEdges = new List<IndexedDependency>();
        var aggregatedDiagnostics = new List<string>();
        var allErrors = new List<ServiceError>();

        foreach (var extractor in extractors)
        {
            var extractionResult = refreshWorkspace
                ? await extractor.Extract(new ExtractionContext(context.TargetPath, context.Progress, context.ChangedFiles), ct)
                : await extractor.ExtractIncremental(context, ct);

            if (extractionResult.IsFailed)
            {
                allErrors.AddRange(extractionResult.Errors.Select(error =>
                    ToServiceError(error)));
                continue;
            }

            var extractedNodes = extractionResult.Value;

            aggregatedProjects.AddRange(extractedNodes.Projects);
            aggregatedCodeNodes.AddRange(extractedNodes.CodeNodes);
            aggregatedEdges.AddRange(extractedNodes.Edges);
            aggregatedDiagnostics.AddRange(extractedNodes.Diagnostics);

            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.project.count", extractedNodes.Projects.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.code_node.count", extractedNodes.CodeNodes.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.dependency.count", extractedNodes.Edges.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.diagnostic.count", extractedNodes.Diagnostics.Count);
        }

        if (allErrors.Count > 0)
        {
            return Result.Fail(allErrors);
        }

        return Result.Ok(new ExtractedNodes(
            [
                .. aggregatedProjects
                    .OrderBy(static project => project.Name, StringComparer.Ordinal)
                    .ThenBy(static project => project.Id, StringComparer.Ordinal)
            ],
            [
                .. aggregatedCodeNodes
                    .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
                    .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            ],
            [
                .. aggregatedEdges
                    .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.EdgeType)
            ],
            aggregatedDiagnostics));
    }

    private static ServiceError ToServiceError(IError error)
    {
        if (error is ServiceError serviceError)
        {
            return serviceError;
        }

        var converted = new ServiceError(ServiceErrorCode.ThirdPartyError, error.Message);
        foreach (var entry in error.Metadata)
        {
            converted.Metadata[entry.Key] = entry.Value;
        }

        return converted;
    }

    private async Task<IReadOnlyList<WorkspaceFileChange>> ExpandDirectoryChanges(
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        CancellationToken ct)
    {
        var expandedChanges = new List<WorkspaceFileChange>(changedFiles.Count);

        foreach (var changedFile in changedFiles)
        {
            switch (changedFile.ActionType)
            {
                case WorkspaceFileChangeAction.DirectoryDeleted:
                    expandedChanges.Add(changedFile);
                    expandedChanges.AddRange(await ExpandDeletedDirectory(changedFile, ct));
                    break;
                case WorkspaceFileChangeAction.DirectoryRenamed:
                    expandedChanges.Add(changedFile);
                    expandedChanges.AddRange(await ExpandRenamedDirectory(changedFile, ct));
                    break;
                default:
                    expandedChanges.Add(changedFile);
                    break;
            }
        }

        return
        [
            .. expandedChanges.Distinct()
        ];
    }

    private ExtractedNodes NormalizePersistedPaths(ExtractedNodes extractedNodes)
    {
        return extractedNodes with
        {
            Projects =
            [
                .. extractedNodes.Projects.Select(
                    project => project with
                    {
                        RelativeFilePath = workspacePaths.ToRepositoryRelativePath(project.RelativeFilePath)
                    })
            ],
            CodeNodes =
            [
                .. extractedNodes.CodeNodes.Select(
                    codeNode => codeNode with
                    {
                        RelativeFilePath = workspacePaths.ToRepositoryRelativePath(codeNode.RelativeFilePath)
                    })
            ]
        };
    }

    private async Task<IReadOnlyList<WorkspaceFileChange>> ExpandDeletedDirectory(
        WorkspaceFileChange changedFile,
        CancellationToken ct)
    {
        if (!workspacePaths.TryToRepositoryRelativePath(changedFile.OldPath, out var oldDirectoryPath))
        {
            return [];
        }

        var persistedPaths = await knowledgeGraphRepository.GetPersistedDocumentPathsUnderDirectory(oldDirectoryPath, ct);

        return
        [
            .. persistedPaths
                .Where(IsIncrementalTargetPath)
                .OrderBy(static path => path, GetPathComparer())
                .Select(path => new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Deleted,
                    OldPath: ToAbsoluteRepositoryPath(path)))
        ];
    }

    private async Task<IReadOnlyList<WorkspaceFileChange>> ExpandRenamedDirectory(
        WorkspaceFileChange changedFile,
        CancellationToken ct)
    {
        var hasOldDirectory = workspacePaths.TryToRepositoryRelativePath(changedFile.OldPath, out var oldDirectoryPath);
        var hasNewDirectory = workspacePaths.TryToRepositoryRelativePath(changedFile.NewPath, out var newDirectoryPath);

        var persistedOldPaths = hasOldDirectory
            ? await knowledgeGraphRepository.GetPersistedDocumentPathsUnderDirectory(oldDirectoryPath, ct)
            : [];
        var discoveredNewPaths = hasNewDirectory
            ? await DiscoverIncrementalFiles(newDirectoryPath, ct)
            : [];
        var pathComparer = GetPathComparer();
        var unmatchedNewPaths = discoveredNewPaths
            .ToHashSet(pathComparer);
        var expandedChanges = new List<WorkspaceFileChange>();

        foreach (var oldPath in persistedOldPaths
                     .Where(IsIncrementalTargetPath)
                     .OrderBy(static path => path, pathComparer))
        {
            var renamedPath = hasOldDirectory && hasNewDirectory
                ? TryMapToRenamedDirectory(oldPath, oldDirectoryPath, newDirectoryPath)
                : null;

            if (renamedPath is not null && unmatchedNewPaths.Remove(renamedPath))
            {
                expandedChanges.Add(
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Renamed,
                        OldPath: ToAbsoluteRepositoryPath(oldPath),
                        NewPath: ToAbsoluteRepositoryPath(renamedPath)));
                continue;
            }

            expandedChanges.Add(
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Deleted,
                    OldPath: ToAbsoluteRepositoryPath(oldPath)));
        }

        expandedChanges.AddRange(
            unmatchedNewPaths
                .Where(IsIncrementalTargetPath)
                .OrderBy(static path => path, pathComparer)
                .Select(path => new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Added,
                    NewPath: ToAbsoluteRepositoryPath(path))));

        return expandedChanges;
    }

    private string[] GetAffectedRelativePaths(IReadOnlyList<WorkspaceFileChange> changedFiles)
    {
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var relativePaths = new HashSet<string>(pathComparer);

        foreach (var changedFile in changedFiles)
        {
            foreach (var affectedPath in changedFile.GetAffectedPaths())
            {
                if (!workspacePaths.TryToRepositoryRelativePath(affectedPath, out var relativePath) ||
                    !IsIncrementalTargetPath(relativePath))
                {
                    continue;
                }

                relativePaths.Add(relativePath);
            }
        }

        return [.. relativePaths.OrderBy(static path => path, pathComparer)];
    }

    private bool RequiresCSharpRefresh(IReadOnlyList<WorkspaceFileChange> changes)
        => changes.Any(change => change.GetAffectedPaths().Any(path =>
            workspacePaths.TryToRepositoryRelativePath(path, out var relativePath) &&
            !WorkspaceIndexingPathRules.IsIgnoredPath(relativePath) &&
            (change.ActionType is WorkspaceFileChangeAction.DirectoryDeleted or WorkspaceFileChangeAction.DirectoryRenamed ||
             CSharpIndexingPathRules.IsRelevantChangePath(relativePath))));

    private bool RequiresTypeScriptRefresh(IReadOnlyList<WorkspaceFileChange> changes)
        => changes.Any(change => change.GetAffectedPaths().Any(path =>
            workspacePaths.TryToRepositoryRelativePath(path, out var relativePath) &&
            !WorkspaceIndexingPathRules.IsIgnoredPath(relativePath) &&
            (string.IsNullOrEmpty(relativePath) || !TypeScriptIndexingPathRules.IsIgnoredPath(relativePath)) &&
            (change.ActionType is WorkspaceFileChangeAction.DirectoryDeleted or WorkspaceFileChangeAction.DirectoryRenamed ||
             TypeScriptIndexingPathRules.IsRelevantChangePath(relativePath))));

    private static bool IsIncrementalTargetPath(string path)
    {
        if (WorkspaceIndexingPathRules.IsIgnoredPath(path))
        {
            return false;
        }

        if (TypeScriptIndexingPathRules.IsTypeScriptFilePath(path))
        {
            return TypeScriptIndexingPathRules.IsIndexedPath(path);
        }

        var extension = Path.GetExtension(path);

        return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
               MarkdownExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<string>> DiscoverIncrementalFiles(
        string relativeDirectoryPath,
        CancellationToken ct)
    {
        var absoluteDirectoryPath = ToAbsoluteRepositoryPath(relativeDirectoryPath);
        var discoveredFiles = await workspaceFileDiscoverer.GetAllowedFiles(
            absoluteDirectoryPath,
            IncrementalDiscoveryGlobs,
            ct);

        return
        [
            .. discoveredFiles
                .Select(static discoveredFile => discoveredFile.RelativeFilePath)
                .Where(IsIncrementalTargetPath)
                .Distinct(GetPathComparer())
                .OrderBy(static path => path, GetPathComparer())
        ];
    }

    private string ToAbsoluteRepositoryPath(string repositoryRelativePath)
        => Path.GetFullPath(Path.Combine(workspacePaths.RootPath, repositoryRelativePath));

    private static string? TryMapToRenamedDirectory(
        string oldPath,
        string oldDirectoryPath,
        string newDirectoryPath)
    {
        var pathComparer = GetPathComparison();

        if (string.IsNullOrEmpty(oldDirectoryPath))
        {
            return string.IsNullOrEmpty(newDirectoryPath)
                ? oldPath
                : $"{newDirectoryPath}/{oldPath}";
        }

        var oldDirectoryPrefix = oldDirectoryPath + "/";
        if (!oldPath.StartsWith(oldDirectoryPrefix, pathComparer))
        {
            return null;
        }

        var relativeSuffix = oldPath[oldDirectoryPrefix.Length..];

        return string.IsNullOrEmpty(newDirectoryPath)
            ? relativeSuffix
            : $"{newDirectoryPath}/{relativeSuffix}";
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private string GetRequiredTargetPath()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(_cliOptions.TargetPath);
        return _cliOptions.TargetPath;
    }
}
