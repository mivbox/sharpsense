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
}
