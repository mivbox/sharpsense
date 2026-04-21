using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing;

public sealed class DocumentDiscoverer(
    IRepositoryWorkspace repositoryWorkspace,
    MarkdownIndexer markdownIndexer)
{
    public async Task<MarkdownIndexResult> Discover(
        string solutionPath,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

        var config = repositoryWorkspace.LoadSharpSenseConfig(solutionPath);
        if (config.IncludePaths.Length == 0)
        {
            return new MarkdownIndexResult([], []);
        }

        var solutionDirectoryPath = repositoryWorkspace.GetRequiredSolutionDirectoryPath(solutionPath);
        var matcher = new Matcher(GetPathComparison());

        foreach (var includePath in config.IncludePaths)
        {
            matcher.AddInclude(repositoryWorkspace.NormalizeDirectorySeparators(includePath));
        }

        var matchResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(solutionDirectoryPath)));
        if (!matchResult.HasMatches)
        {
            return new MarkdownIndexResult([], []);
        }

        var matchedRelativePaths = matchResult.Files
            .Select(static match => match.Path.Replace('\\', '/'))
            .Distinct(GetPathComparer())
            .OrderBy(static path => path, GetPathComparer())
            .ToArray();
        var documentNodes = new List<CodeNode>();
        var documentEdges = new List<Domain.KnowledgeGraph.Edges.DependencyEdge>();

        foreach (var relativeFilePath in matchedRelativePaths)
        {
            ct.ThrowIfCancellationRequested();

            var absoluteFilePath = Path.GetFullPath(Path.Combine(solutionDirectoryPath, relativeFilePath));
            var rawText = await File.ReadAllTextAsync(absoluteFilePath, ct);
            var repositoryRelativeFilePath = repositoryWorkspace.ToRepositoryRelativePath(absoluteFilePath);
            var indexResult = markdownIndexer.Index(rawText, repositoryRelativeFilePath);
            documentNodes.AddRange(indexResult.CodeNodes);
            documentEdges.AddRange(indexResult.Edges);
        }

        return new MarkdownIndexResult(
            [
                .. documentNodes
                    .OrderBy(static node => node.RelativeFilePath, GetPathComparer())
                    .ThenBy(static node => node.StartLine)
                    .ThenBy(static node => node.Id, StringComparer.Ordinal)
            ],
            [
                .. documentEdges
                    .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.EdgeType)
            ]);
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
