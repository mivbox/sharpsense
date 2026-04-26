using GitIgnore = Ignore.Ignore;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing;

public sealed class WorkspaceFileDiscoverer(IRepositoryWorkspace repositoryWorkspace) : IWorkspaceFileDiscoverer
{
    public Task<IReadOnlyList<DiscoveredFile>> GetAllowedFiles(
        string targetDirectory,
        IReadOnlyList<string> includeGlobs,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentNullException.ThrowIfNull(includeGlobs);

        ct.ThrowIfCancellationRequested();

        var absoluteTargetDirectory = Path.GetFullPath(targetDirectory);
        if (!repositoryWorkspace.IsSameOrSubPath(absoluteTargetDirectory))
        {
            throw new InvalidOperationException(
                $"Target directory '{absoluteTargetDirectory}' must be located under repository root '{repositoryWorkspace.RootPath}'.");
        }

        if (!Directory.Exists(absoluteTargetDirectory))
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

        var matchResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(absoluteTargetDirectory)));
        if (!matchResult.HasMatches)
        {
            return Task.FromResult<IReadOnlyList<DiscoveredFile>>([]);
        }

        var ignoreEngine = CreateIgnoreEngine();
        var discoveredFilesByRelativePath = new Dictionary<string, DiscoveredFile>(GetPathComparer());

        foreach (var match in matchResult.Files.OrderBy(static match => match.Path, GetPathComparer()))
        {
            ct.ThrowIfCancellationRequested();

            var absolutePath = Path.GetFullPath(Path.Combine(absoluteTargetDirectory, match.Path));
            if (!File.Exists(absolutePath))
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
        var gitIgnorePath = Path.Combine(repositoryWorkspace.RootPath, ".gitignore");
        if (!File.Exists(gitIgnorePath))
        {
            return ignoreEngine;
        }

        return ignoreEngine.Add(File.ReadAllLines(gitIgnorePath));
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
