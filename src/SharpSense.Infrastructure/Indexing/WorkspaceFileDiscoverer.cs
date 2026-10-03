using Microsoft.Extensions.FileSystemGlobbing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Indexing;

internal sealed class WorkspaceFileDiscoverer(
    IRepositoryWorkspace repositoryWorkspace,
    IFileSystem fileSystem) : IWorkspaceFileDiscoverer
{
    public Task<IReadOnlyList<DiscoveredFile>> GetAllowedFiles(
        string targetDirectory,
        IReadOnlyList<string> includeGlobs,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentNullException.ThrowIfNull(includeGlobs);

        ct.ThrowIfCancellationRequested();

        var absoluteTargetDirectory = RepositoryWorkspace.NormalizeRootPath(targetDirectory, fileSystem);
        if (!repositoryWorkspace.IsSameOrSubPath(absoluteTargetDirectory))
        {
            throw new InvalidOperationException(
                $"Target directory '{absoluteTargetDirectory}' must be located under repository root '{repositoryWorkspace.RootPath}'.");
        }

        if (!fileSystem.Directory.Exists(absoluteTargetDirectory))
        {
            return Task.FromResult<IReadOnlyList<DiscoveredFile>>([]);
        }

        var normalizedGlobs = includeGlobs
            .Where(static includeGlob => !string.IsNullOrWhiteSpace(includeGlob))
            .Select(includeGlob => repositoryWorkspace.NormalizeDirectorySeparators(includeGlob.Trim()))
            .ToArray();
        if (normalizedGlobs.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<DiscoveredFile>>([]);
        }

        var matcher = new Matcher(FileSystemPaths.Comparison);
        foreach (var includeGlob in normalizedGlobs)
        {
            matcher.AddInclude(includeGlob);
        }

        var ignoreEngine = new WorkspaceIgnoreRules(fileSystem, repositoryWorkspace.RootPath);
        var discoveredFilesByRelativePath = new Dictionary<string, DiscoveredFile>(FileSystemPaths.Comparer);

        foreach (var absolutePath in EnumerateFiles(absoluteTargetDirectory, ignoreEngine, ct))
        {
            ct.ThrowIfCancellationRequested();

            if (!fileSystem.File.Exists(absolutePath))
            {
                continue;
            }

            var targetRelativePath = repositoryWorkspace.NormalizeDirectorySeparators(
                fileSystem.Path.GetRelativePath(absoluteTargetDirectory, absolutePath));
            if (!matcher.Match(targetRelativePath).HasMatches)
            {
                continue;
            }

            var relativePath = repositoryWorkspace.ToRepositoryRelativePath(absolutePath);
            var normalizedRelativePath = repositoryWorkspace.NormalizeDirectorySeparators(relativePath);
            if (ignoreEngine.IsIgnored(normalizedRelativePath))
            {
                continue;
            }

            discoveredFilesByRelativePath[normalizedRelativePath] = new DiscoveredFile(
                absolutePath,
                normalizedRelativePath);
        }

        return Task.FromResult<IReadOnlyList<DiscoveredFile>>(
            [
                .. discoveredFilesByRelativePath.Values
                    .OrderBy(static file => file.RelativeFilePath, FileSystemPaths.Comparer)
            ]);
    }

    private IEnumerable<string> EnumerateFiles(string root, WorkspaceIgnoreRules ignoreRules, CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var file in fileSystem.Directory.EnumerateFiles(directory))
            {
                ct.ThrowIfCancellationRequested();
                if ((fileSystem.FileInfo.New(file).Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    yield return file;
                }
            }

            foreach (var child in fileSystem.Directory.EnumerateDirectories(directory))
            {
                ct.ThrowIfCancellationRequested();
                var relativePath = fileSystem.Path.GetRelativePath(repositoryWorkspace.RootPath, child);
                if (!ignoreRules.IsIgnored(relativePath, directory: true) &&
                    (fileSystem.DirectoryInfo.New(child).Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }
    }
}
