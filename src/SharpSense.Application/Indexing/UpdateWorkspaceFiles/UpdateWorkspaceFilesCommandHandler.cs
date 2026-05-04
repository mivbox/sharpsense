using Microsoft.Extensions.Options;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;

namespace SharpSense.Application.Indexing.UpdateWorkspaceFiles;

public sealed class UpdateWorkspaceFilesCommandHandler(
    IEnumerable<ILanguageExtractor> extractors,
    IKnowledgeGraphRepository knowledgeGraphRepository,
    IIndexingWorkspacePaths workspacePaths,
    IWorkspaceFileDiscoverer workspaceFileDiscoverer,
    IOptions<SharpSenseCliOptions> cliOptions)
    : ICommandHandler<UpdateWorkspaceFilesCommand>
{
    private static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdown", ".mkd"];
    private static readonly string[] IncrementalDiscoveryGlobs = ["**/*.cs", "**/*.md", "**/*.markdown", "**/*.mdown", "**/*.mkd"];
    private readonly SharpSenseCliOptions _cliOptions = cliOptions?.Value ?? throw new ArgumentNullException(nameof(cliOptions));

    public async Task Handle(UpdateWorkspaceFilesCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.ChangedFiles);

        var absoluteTargetPath = workspacePaths.GetRequiredTargetPath(GetRequiredTargetPath());
        var expandedChangedFiles = await ExpandDirectoryChanges(command.ChangedFiles, ct);
        var changedFilePaths = GetAffectedRelativePaths(expandedChangedFiles);

        using var trace = SharpSenseTraceSpan.Start("index.target.incremental");
        trace.AddTag("target.path", absoluteTargetPath);
        trace.AddTag("repository.root", workspacePaths.RootPath);
        trace.AddTag("index.change.count", command.ChangedFiles.Count);
        trace.AddTag("index.expanded_change.count", expandedChangedFiles.Count);

        try
        {
            if (expandedChangedFiles.Count == 0 || changedFilePaths.Length == 0)
            {
                return;
            }

            using var extractActivity = SharpSenseTraceSpan.Start("index.extract.incremental");
            var extractedNodes = await Extract(
                new IncrementalExtractionContext(absoluteTargetPath, expandedChangedFiles, command.Progress),
                extractActivity,
                ct);

            extractedNodes = NormalizePersistedPaths(extractedNodes);
            command.Progress?.Report(new IndexingProgress("Persisting incremental index...", changedFilePaths.Length, changedFilePaths.Length));

            await knowledgeGraphRepository.ReplaceWorkspaceFiles(changedFilePaths, extractedNodes, ct);

            trace.AddTag("index.code_node.count", extractedNodes.CodeNodes.Count);
            trace.AddTag("index.dependency.count", extractedNodes.Edges.Count);
            trace.AddTag("index.diagnostic.count", extractedNodes.Diagnostics.Count);
        }
        catch (Exception exception)
        {
            trace.RecordExceptionAndErrorStatus(exception);
            throw;
        }
    }

    private async Task<ExtractedNodes> Extract(
        IncrementalExtractionContext context,
        SharpSenseTraceSpan extractActivity,
        CancellationToken ct)
    {
        var aggregatedProjects = new List<IndexedProject>();
        var aggregatedCodeNodes = new List<IndexedCodeNode>();
        var aggregatedEdges = new List<IndexedDependency>();
        var aggregatedDiagnostics = new List<string>();

        foreach (var extractor in extractors)
        {
            var extractedNodes = await extractor.ExtractIncremental(context, ct);

            aggregatedProjects.AddRange(extractedNodes.Projects);
            aggregatedCodeNodes.AddRange(extractedNodes.CodeNodes);
            aggregatedEdges.AddRange(extractedNodes.Edges);
            aggregatedDiagnostics.AddRange(extractedNodes.Diagnostics);

            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.project.count", extractedNodes.Projects.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.code_node.count", extractedNodes.CodeNodes.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.dependency.count", extractedNodes.Edges.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.diagnostic.count", extractedNodes.Diagnostics.Count);
        }

        return new ExtractedNodes(
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
            aggregatedDiagnostics);
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
                    expandedChanges.AddRange(await ExpandDeletedDirectory(changedFile, ct));
                    break;
                case WorkspaceFileChangeAction.DirectoryRenamed:
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

    private static bool IsIncrementalTargetPath(string path)
    {
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
                .Where(path => !WorkspaceIndexingPathRules.IsIgnoredPath(path))
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
