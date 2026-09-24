using Microsoft.Extensions.Options;
using System.IO.Abstractions;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing;

public sealed class DocumentDiscoverer(
    IRepositoryWorkspace repositoryWorkspace,
    IOptionsMonitor<SharpSenseConfig> configMonitor,
    IWorkspaceFileDiscoverer fileDiscoverer,
    IMarkdownIndexer markdownIndexer,
    IFileSystem fileSystem)
{
    public async Task<MarkdownIndexResult> Discover(
        string targetPath,
        CancellationToken ct,
        IReadOnlyList<string>? includePatterns = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var includePaths = includePatterns?.ToArray() ?? GetIncludePaths();
        if (includePaths.Length == 0)
        {
            return new MarkdownIndexResult([], []);
        }

        var targetDirectoryPath = repositoryWorkspace.GetRequiredTargetDirectoryPath(targetPath);
        var discoveredFiles = await fileDiscoverer.GetAllowedFiles(targetDirectoryPath, includePaths, ct);
        if (discoveredFiles.Count == 0)
        {
            return new MarkdownIndexResult([], []);
        }

        return await DiscoverFiles(discoveredFiles, ct);
    }

    public async Task<MarkdownIndexResult> DiscoverFiles(
        string targetPath,
        IReadOnlyList<string> filePaths,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(filePaths);

        var includePaths = GetIncludePaths();
        if (includePaths.Length == 0)
        {
            return new MarkdownIndexResult([], []);
        }

        var targetDirectoryPath = repositoryWorkspace.GetRequiredTargetDirectoryPath(targetPath);
        var allowedFiles = await fileDiscoverer.GetAllowedFiles(targetDirectoryPath, includePaths, ct);
        if (allowedFiles.Count == 0)
        {
            return new MarkdownIndexResult([], []);
        }

        var requestedPaths = filePaths
            .Where(static filePath => !string.IsNullOrWhiteSpace(filePath))
            .Select(ResolveAbsolutePath)
            .ToHashSet(GetPathComparer());
        if (requestedPaths.Count == 0)
        {
            return new MarkdownIndexResult([], []);
        }

        return await DiscoverFiles(
            [
                .. allowedFiles.Where(discoveredFile => requestedPaths.Contains(discoveredFile.AbsolutePath))
            ],
            ct);
    }

    private async Task<MarkdownIndexResult> DiscoverFiles(
        IReadOnlyList<DiscoveredFile> discoveredFiles,
        CancellationToken ct)
    {
        var documentNodes = new List<CodeNode>();
        var documentEdges = new List<Domain.KnowledgeGraph.Edges.DependencyEdge>();
        var orderedFiles = discoveredFiles
            .Where(static file => !string.IsNullOrWhiteSpace(file.AbsolutePath) &&
                                  !string.IsNullOrWhiteSpace(file.RelativeFilePath))
            .Where(file => fileSystem.File.Exists(file.AbsolutePath))
            .GroupBy(static file => file.RelativeFilePath, GetPathComparer())
            .Select(static group => group.First())
            .OrderBy(static file => file.RelativeFilePath, GetPathComparer())
            .ToArray();

        foreach (var discoveredFile in orderedFiles)
        {
            ct.ThrowIfCancellationRequested();

            var rawText = await fileSystem.File.ReadAllTextAsync(discoveredFile.AbsolutePath, ct);
            var indexResult = markdownIndexer.Index(rawText, discoveredFile.RelativeFilePath);
            documentNodes.AddRange(indexResult.CodeNodes);
            documentEdges.AddRange(indexResult.Edges);
        }

        return new MarkdownIndexResult(
            [
                .. documentNodes
                    .OrderBy(static node => node.RelativeFilePath, GetPathComparer())
                    .ThenBy(static node => node.StartLine)
                    .ThenBy(static node => node.CanonicalId, StringComparer.Ordinal)
             ],
            [
                .. documentEdges
                    .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.EdgeType)
             ]);
    }

    private string ResolveAbsolutePath(string filePath)
        => fileSystem.Path.GetFullPath(
            fileSystem.Path.IsPathRooted(filePath)
                ? filePath
                : fileSystem.Path.Combine(repositoryWorkspace.RootPath, filePath));

    private string[] GetIncludePaths()
    {
        var includePaths = configMonitor.CurrentValue.IncludePaths;
        return includePaths.Length == 0 ? [] : [.. includePaths];
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
