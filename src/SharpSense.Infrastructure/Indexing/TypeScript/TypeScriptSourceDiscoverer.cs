using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Storage;
using SharpSense.TypeScript.Parsing;
using SharpSense.TypeScript.Parsing.Models;
using System.IO.Abstractions;

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

    public async Task<IReadOnlyList<TypeScriptParsedFile>> Discover(
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
                .GroupBy(static file => file.RelativeFilePath, FileSystemPaths.Comparer)
                .Select(static group => group.First())
                .OrderBy(static file => file.RelativeFilePath, FileSystemPaths.Comparer)
        ];
    }

    private async Task<IReadOnlyList<DiscoveredFile>> GetTargetFiles(
        string targetPath,
        CancellationToken ct)
    {
        var discoveredFiles = new List<DiscoveredFile>();
        foreach (var scope in new[] { targetPath }
            .Concat(tsConfigResolver.GetReferencedConfigPaths(targetPath)))
        {
            var targetDirectoryPath = repositoryWorkspace.GetRequiredTargetDirectoryPath(scope);
            // Configured files/includes can point above the configuration directory. Start from
            // repository-owned, nonignored candidates, then apply that configuration's root rules.
            var files = tsConfigResolver.HasTargetConfiguration(scope)
                ? (await GetRepositoryFilesByAbsolutePath(ct)).Values.ToArray()
                : await fileDiscoverer.GetAllowedFiles(
                    targetDirectoryPath,
                    TypeScriptIndexingPathRules.IncludeGlobs,
                    ct);
            discoveredFiles.AddRange(tsConfigResolver.FilterRootFiles(scope, files));
        }

        return FilterIndexedFiles(discoveredFiles);
    }

    private async Task<IReadOnlyList<TypeScriptParsedFile>> ExpandReachableFiles(
        IReadOnlyList<DiscoveredFile> seedFiles,
        CancellationToken ct)
    {
        if (seedFiles.Count == 0)
        {
            return [];
        }

        var discoveredFilesByAbsolutePath = seedFiles
            .ToDictionary(static file => file.AbsolutePath, FileSystemPaths.Comparer);
        var pendingFiles = new Queue<DiscoveredFile>(seedFiles);
        var parsedFiles = new List<TypeScriptParsedFile>(seedFiles.Count);

        while (pendingFiles.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var discoveredFile = pendingFiles.Dequeue();
            string sourceText;
            try
            {
                sourceText = await fileSystem.File.ReadAllTextAsync(discoveredFile.AbsolutePath, ct);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new TypeScriptSourceException(discoveredFile.RelativeFilePath, exception.Message, exception);
            }

            TypeScriptSyntaxTree tree;
            try
            {
                tree = TypeScriptParser.Parse(discoveredFile.RelativeFilePath, sourceText, ct);
            }
            catch (TypeScriptParseLimitException exception)
            {
                throw new TypeScriptSourceException(discoveredFile.RelativeFilePath, exception.Message, exception);
            }

            parsedFiles.Add(new TypeScriptParsedFile(discoveredFile, tree));
            foreach (var node in tree.Root.Statements ?? [])
            {
                var importPath = TypeScriptSyntax.GetModuleSpecifier(node);
                if (string.IsNullOrWhiteSpace(importPath))
                {
                    continue;
                }

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
            .. parsedFiles
                .OrderBy(static file => file.DiscoveredFile.RelativeFilePath, FileSystemPaths.Comparer)
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
            .ToDictionary(static file => file.AbsolutePath, FileSystemPaths.Comparer);

        return _repositoryFilesByAbsolutePath;
    }
}
