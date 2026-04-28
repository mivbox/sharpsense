using GitIgnore = Ignore.Ignore;
using Microsoft.Extensions.FileSystemGlobbing;
using System.IO.Abstractions;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing;

public sealed class WorkspaceFileDiscoverer(
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

        var absoluteTargetDirectory = fileSystem.Path.GetFullPath(targetDirectory);
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

        var matcher = new Matcher(GetPathComparison());
        foreach (var includeGlob in normalizedGlobs)
        {
            matcher.AddInclude(includeGlob);
        }

        var ignoreEngine = CreateIgnoreEngine();
        var discoveredFilesByRelativePath = new Dictionary<string, DiscoveredFile>(GetPathComparer());

        foreach (var absolutePath in fileSystem.Directory
                     .EnumerateFiles(absoluteTargetDirectory, "*", SearchOption.AllDirectories)
                     .OrderBy(static path => path, GetPathComparer()))
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
                    .OrderBy(static file => file.RelativeFilePath, GetPathComparer())
            ]);
    }

    private GitIgnore CreateIgnoreEngine()
    {
        var ignoreEngine = new GitIgnore();
        var gitIgnorePath = fileSystem.Path.Combine(repositoryWorkspace.RootPath, ".gitignore");
        if (!fileSystem.File.Exists(gitIgnorePath))
        {
            return ignoreEngine;
        }

        return ignoreEngine.Add(fileSystem.File.ReadAllLines(gitIgnorePath));
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
