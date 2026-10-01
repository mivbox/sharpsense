using FluentResults;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.Indexing.Markdown;

internal sealed class MarkdownDocumentExtractor(DocumentDiscoverer documentDiscoverer) : ILanguageExtractor
{
    public WorkspaceSourceKind SourceKind => WorkspaceSourceKind.Markdown;

    public async Task<Result<ExtractedNodes>> Extract(
        ExtractionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TargetPath);

        var discoveredDocuments = await documentDiscoverer.Discover(
            context.TargetPath,
            ct,
            context.IncludePatterns ?? []);

        return Result.Ok(new ExtractedNodes(
            [],
            [.. discoveredDocuments.CodeNodes.Select(ToIndexedCodeNode)],
            [.. discoveredDocuments.Edges.Select(ToIndexedDependency)],
            []));
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
            string.IsNullOrWhiteSpace(codeNode.Summary)
                ? codeNode.DisplayName
                : $"{codeNode.DisplayName}\n{codeNode.Summary}",
            codeNode.BodyHash,
            codeNode.VectorEmbedding);

    private static IndexedDependency ToIndexedDependency(DependencyEdge dependencyEdge)
        => new(
            dependencyEdge.CallerId,
            dependencyEdge.CalleeId,
            dependencyEdge.EdgeType);
}
