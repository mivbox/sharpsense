using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using TreeSitter;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TypeScriptSourceDiscoverer(
    IRepositoryWorkspace repositoryWorkspace,
    IWorkspaceFileDiscoverer fileDiscoverer,
    IFileSystem fileSystem,
    TsConfigResolver tsConfigResolver)
{
    private Dictionary<string, DiscoveredFile>? _repositoryFilesByAbsolutePath;

    public IReadOnlyCollection<string> ResolutionInputPaths => tsConfigResolver.ResolutionInputPaths;

    internal void ClearCache()
    {
        _repositoryFilesByAbsolutePath = null;
        tsConfigResolver.ClearCache();
    }

    public async Task<IReadOnlyList<DiscoveredFile>> Discover(
        string targetPath,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        // Scoped services can still be reused across multiple commands in an integration host.
        ClearCache();
        try
        {
            var seedFiles = await GetTargetFiles(targetPath, ct);

            return await ExpandReachableFiles(seedFiles, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ClearCache();
            throw;
        }
    }

    private IReadOnlyList<DiscoveredFile> FilterIndexedFiles(IReadOnlyList<DiscoveredFile> discoveredFiles)
    {
        ArgumentNullException.ThrowIfNull(discoveredFiles);

        return
        [
            .. discoveredFiles
                .Where(static file => !string.IsNullOrWhiteSpace(file.AbsolutePath) &&
                    !string.IsNullOrWhiteSpace(file.RelativeFilePath))
                .Where(file => TypeScriptIndexingPathRules.IsIndexedPath(file.RelativeFilePath))
                .GroupBy(static file => file.RelativeFilePath, GetPathComparer())
                .Select(static group => group.First())
                .OrderBy(static file => file.RelativeFilePath, GetPathComparer())
        ];
    }

    private async Task<IReadOnlyList<DiscoveredFile>> GetTargetFiles(
        string targetPath,
        CancellationToken ct)
    {
        var discoveredFiles = new List<DiscoveredFile>();
        foreach (var scope in new[]
        {
            targetPath
        }.Concat(tsConfigResolver.GetReferencedConfigPaths(targetPath)))
        {
            var targetDirectoryPath = repositoryWorkspace.GetRequiredTargetDirectoryPath(scope);
            // Configured files/includes can point above the configuration directory. Start from
            // repository-owned, nonignored candidates, then apply that configuration's root rules.
            var files = tsConfigResolver.HasTargetConfiguration(scope)
                ? (await GetRepositoryFilesByAbsolutePath(ct)).Values.ToArray()
                : await fileDiscoverer.GetAllowedFiles(targetDirectoryPath, TypeScriptIndexingPathRules.IncludeGlobs, ct);
            discoveredFiles.AddRange(tsConfigResolver.FilterRootFiles(scope, files));
        }

        return FilterIndexedFiles(discoveredFiles);
    }

    private async Task<IReadOnlyList<DiscoveredFile>> ExpandReachableFiles(
        IReadOnlyList<DiscoveredFile> seedFiles,
        CancellationToken ct)
    {
        if (seedFiles.Count == 0)
        {
            return [];
        }

        var discoveredFilesByAbsolutePath = seedFiles
            .ToDictionary(static file => file.AbsolutePath, GetPathComparer());
        var pendingFiles = new Queue<DiscoveredFile>(seedFiles);

        while (pendingFiles.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var discoveredFile = pendingFiles.Dequeue();
            var sourceText = await fileSystem.File.ReadAllTextAsync(discoveredFile.AbsolutePath, ct);
            foreach (var importPath in ExtractImportPaths(sourceText, discoveredFile.RelativeFilePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)))
            {
                var resolvedImportPath = tsConfigResolver.ResolveImport(importPath, discoveredFile.AbsolutePath, ct);
                if (string.IsNullOrWhiteSpace(resolvedImportPath))
                {
                    continue;
                }

                if (discoveredFilesByAbsolutePath.ContainsKey(resolvedImportPath))
                {
                    continue;
                }

                if (!repositoryWorkspace.IsSameOrSubPath(resolvedImportPath))
                {
                    continue;
                }

                var repositoryFilesByAbsolutePath = await GetRepositoryFilesByAbsolutePath(ct);
                if (!repositoryFilesByAbsolutePath.TryGetValue(resolvedImportPath, out var importedFile))
                {
                    continue;
                }

                discoveredFilesByAbsolutePath[importedFile.AbsolutePath] = importedFile;
                pendingFiles.Enqueue(importedFile);
            }
        }

        return
        [
            .. discoveredFilesByAbsolutePath.Values
                .OrderBy(static file => file.RelativeFilePath, GetPathComparer())
        ];
    }

    private async Task<IReadOnlyDictionary<string, DiscoveredFile>> GetRepositoryFilesByAbsolutePath(CancellationToken ct)
    {
        if (_repositoryFilesByAbsolutePath is not null)
        {
            return _repositoryFilesByAbsolutePath;
        }

        var repositoryFiles = FilterIndexedFiles(
            await fileDiscoverer.GetAllowedFiles(
                repositoryWorkspace.RootPath,
                TypeScriptIndexingPathRules.IncludeGlobs,
                ct));
        _repositoryFilesByAbsolutePath = repositoryFiles
            .ToDictionary(static file => file.AbsolutePath, GetPathComparer());

        return _repositoryFilesByAbsolutePath;
    }

    private static IEnumerable<string> ExtractImportPaths(string sourceText, bool isTsx)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        using var language = new Language(isTsx ? "TSX" : "TypeScript");
        using var parser = new Parser(language);
        using var tree = parser.Parse(sourceText);
        if (tree is null)
        {
            yield break;
        }
        foreach (var node in tree.RootNode.NamedChildren.Where(node => node.Type is "import_statement" or "export_statement"))
        {
            var path = node.GetChildForField("source")?.Text.Trim('\'', '"');
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
