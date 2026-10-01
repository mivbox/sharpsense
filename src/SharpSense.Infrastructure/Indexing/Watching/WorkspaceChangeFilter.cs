using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Options;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing.Watching;

/// <summary>
/// Narrows repository watcher batches to selected sources and discovered semantic dependencies.
/// Retaining old dependencies until process restart also handles deletes and failed reloads safely.
/// </summary>
internal sealed class WorkspaceChangeFilter(
    IIndexingWorkspacePaths workspacePaths,
    IOptions<WorkspaceExecutionOptions> options) : IWorkspaceChangeFilter
{
    private readonly StringComparer _pathComparer = FileSystemPaths.Comparer;
    private readonly Dictionary<WorkspaceSourceKind, HashSet<string>> _trackedFiles = [];
    private readonly Dictionary<WorkspaceSourceKind, HashSet<string>> _trackedDirectories = [];
    private readonly Dictionary<WorkspaceSourceKind, HashSet<string>> _declaredInputs = [];

    public bool IsRelevant(IReadOnlyList<WorkspaceFileChange> changes)
    {
        var sources = options.Value.WorkspaceSources;
        if (sources.Count == 0)
        {
            return true;
        }

        var markdownMatcher = new Matcher(FileSystemPaths.Comparison);
        markdownMatcher.AddIncludePatterns(sources
            .Where(static source => source.Kind == WorkspaceSourceKind.Markdown)
            .Select(static source => source.Path));

        foreach (var change in changes)
        {
            foreach (var path in change.GetAffectedPaths())
            {
                if (!workspacePaths.TryToRepositoryRelativePath(path, out var relativePath) ||
                    WorkspaceIndexingPathRules.IsIgnoredPath(relativePath))
                {
                    continue;
                }

                var directoryChange = change.ActionType is WorkspaceFileChangeAction.DirectoryDeleted
                    or WorkspaceFileChangeAction.DirectoryRenamed;
                // Directory events can remove/move selected documentation without individual
                // file events. Full reconciliation is intentionally conservative in that case.
                if (directoryChange && sources.Any(static source => source.Kind == WorkspaceSourceKind.Markdown))
                {
                    return true;
                }

                if (!directoryChange && markdownMatcher.Match(relativePath).HasMatches)
                {
                    return true;
                }

                foreach (var source in sources.Where(static source => source.Kind != WorkspaceSourceKind.Markdown))
                {
                    if (IsRelevantSourcePath(source, relativePath, directoryChange, change.ActionType))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public void TrackSource(WorkspaceSource source, ExtractedNodes extractedNodes)
    {
        var files = GetPaths(_trackedFiles, source.Kind);
        var directories = GetPaths(_trackedDirectories, source.Kind);
        var declaredInputs = GetPaths(_declaredInputs, source.Kind, StringComparer.OrdinalIgnoreCase);
        foreach (var path in extractedNodes.CodeNodes.Select(static node => node.RelativeFilePath))
        {
            if (workspacePaths.TryToRepositoryRelativePath(path, out var relativePath))
            {
                files.Add(relativePath);
            }
        }

        foreach (var path in extractedNodes.InputPaths ?? [])
        {
            if (workspacePaths.TryToRepositoryRelativePath(path, out var relativePath))
            {
                files.Add(relativePath);
                declaredInputs.Add(relativePath);
            }
        }

        foreach (var project in extractedNodes.Projects)
        {
            if (workspacePaths.TryToRepositoryRelativePath(project.RelativeFilePath, out var relativePath))
            {
                files.Add(relativePath);
                directories.Add(Normalize(Path.GetDirectoryName(relativePath) ?? string.Empty));
            }
        }
    }

    private bool IsRelevantSourcePath(
        WorkspaceSource source,
        string path,
        bool directoryChange,
        WorkspaceFileChangeAction action)
    {
        // Declared semantic inputs can have arbitrary extensions, including Markdown
        // generator inputs outside documentation selections. Check them before language filters.
        var files = GetPaths(_trackedFiles, source.Kind);
        var declaredInputs = GetPaths(_declaredInputs, source.Kind, StringComparer.OrdinalIgnoreCase);
        // Only invalidation of already-declared inputs is conservative about casing.
        // Repository containment and selected source identity retain their original rules.
        if (declaredInputs.Contains(path) || directoryChange && declaredInputs
            .Any(input =>
                input.StartsWith(
                    Normalize(path)
                        .TrimEnd('/') + "/",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (files.Contains(path) || directoryChange && files.Any(file => IsWithin(file, path)))
        {
            return true;
        }

        // A newly added/renamed Markdown file may match a previously empty AdditionalFiles
        // glob. Its membership is unknown until MSBuild evaluates the C# source again.
        if (source.Kind == WorkspaceSourceKind.CSharp &&
            action is WorkspaceFileChangeAction.Added or WorkspaceFileChangeAction.Renamed &&
            Path.GetExtension(path)
                .ToLowerInvariant() is ".md" or ".markdown" or ".mdown" or ".mkd")
        {
            return true;
        }

        // Build imports and TypeScript extends/references can live outside the selected
        // folders. Reevaluate those configuration files before attempting narrower filtering.
        if (!directoryChange &&
            (source.Kind == WorkspaceSourceKind.CSharp && CSharpIndexingPathRules.IsConfigurationPath(path) ||
             source.Kind == WorkspaceSourceKind.TypeScript && Path.GetExtension(path)
                 .Equals(".json", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!directoryChange && !(source.Kind switch
        {
            WorkspaceSourceKind.CSharp => CSharpIndexingPathRules.IsRelevantChangePath(path),
            WorkspaceSourceKind.TypeScript => TypeScriptIndexingPathRules.IsRelevantChangePath(path),
            _ => false
        }))
        {
            return false;
        }

        var directories = GetPaths(_trackedDirectories, source.Kind);
        if (directories.Any(directory => IsWithin(path, directory) || directoryChange && IsWithin(directory, path)))
        {
            return true;
        }

        var sourcePath = workspacePaths.ToRepositoryRelativePath(workspacePaths.GetRequiredTargetPath(source.Path));
        var sourceDirectory = source.Kind == WorkspaceSourceKind.CSharp ||
                              Path.GetExtension(sourcePath)
                                  .Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? Normalize(Path.GetDirectoryName(sourcePath) ?? string.Empty)
            : sourcePath;
        // Solution selections may contain projects outside the solution's own directory.
        // Before their first successful extraction, conservatively retain C# source changes.
        if (files.Count == 0 && source.Kind == WorkspaceSourceKind.CSharp &&
            !Path.GetExtension(sourcePath)
                .Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsWithin(path, sourceDirectory) || directoryChange && IsWithin(sourceDirectory, path);
    }

    private HashSet<string> GetPaths(
        Dictionary<WorkspaceSourceKind, HashSet<string>> paths,
        WorkspaceSourceKind kind,
        StringComparer? comparer = null)
    {
        if (!paths.TryGetValue(kind, out var value))
        {
            value = new HashSet<string>(comparer ?? _pathComparer);
            paths[kind] = value;
        }

        return value;
    }

    private bool IsWithin(string path, string directory)
    {
        path = Normalize(path);
        directory = Normalize(directory)
            .TrimEnd('/');

        return directory is "" or "." || _pathComparer.Equals(path, directory) ||
            path.StartsWith(
                directory + "/",
                FileSystemPaths.Comparison);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
