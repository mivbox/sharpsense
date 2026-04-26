using SharpSense.Application.Features.Indexing.Contracts;

namespace SharpSense.Infrastructure.Indexing.Markdown;

public sealed class MarkdownDocumentExtractor(DocumentDiscoverer documentDiscoverer) : ILanguageExtractor
{
    public string ExtractorName => "markdown";

    public async Task<ExtractedNodes> Extract(
        ExtractionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TargetPath);

        var discoveredDocuments = await documentDiscoverer.Discover(context.TargetPath, ct);

        return new ExtractedNodes([], discoveredDocuments.CodeNodes, discoveredDocuments.Edges, []);
    }

    public async Task<ExtractedNodes> ExtractIncremental(
        IncrementalExtractionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TargetPath);
        ArgumentNullException.ThrowIfNull(context.ChangedFiles);

        var changedMarkdownFiles = context.ChangedFiles
            .Select(static changedFile => changedFile.GetCurrentPath())
            .Where(path => !string.IsNullOrWhiteSpace(path) && MarkdownIndexer.IsMarkdownDocumentPath(path))
            .Select(static path => path!)
            .Distinct(GetPathComparer())
            .ToArray();
        if (changedMarkdownFiles.Length == 0)
        {
            return new ExtractedNodes([], [], [], []);
        }

        var discoveredDocuments = await documentDiscoverer.DiscoverFiles(context.TargetPath, changedMarkdownFiles, ct);

        return new ExtractedNodes([], discoveredDocuments.CodeNodes, discoveredDocuments.Edges, []);
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
