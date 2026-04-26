using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

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

        return new ExtractedNodes(
            [],
            [.. discoveredDocuments.CodeNodes.Select(ToIndexedCodeNode)],
            [.. discoveredDocuments.Edges.Select(ToIndexedDependency)],
            []);
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

        return new ExtractedNodes(
            [],
            [.. discoveredDocuments.CodeNodes.Select(ToIndexedCodeNode)],
            [.. discoveredDocuments.Edges.Select(ToIndexedDependency)],
            []);
    }

    private static IndexedCodeNode ToIndexedCodeNode(CodeNode codeNode)
        => new(
            codeNode.CanonicalId,
            codeNode.ProjectId,
            codeNode.FullyQualifiedName,
            codeNode.DisplayName,
            codeNode.NodeType,
            codeNode.RelativeFilePath,
            codeNode.StartLine,
            codeNode.EndLine,
            codeNode.Summary,
            codeNode.VectorEmbedding);

    private static IndexedDependency ToIndexedDependency(DependencyEdge dependencyEdge)
        => new(
            dependencyEdge.CallerId,
            dependencyEdge.CalleeId,
            dependencyEdge.EdgeType);

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
