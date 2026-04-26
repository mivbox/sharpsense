using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.Markdown;

namespace SharpSense.Infrastructure.Tests.Indexing.Markdown;

public sealed class MarkdownIndexerTests
{
    [Fact]
    public void WhenIndexingMarkdownWithDuplicateHeadings_ThenChunksByHeadingAndDisambiguatesSlugsWithStartLine()
    {
        var rawText =
            """
            Intro paragraph.

            # Overview
            Overview body.

            ## Details
            Detail body.

            # Overview
            Repeated body.
            """;
        var indexer = new MarkdownIndexer();
        var result = indexer.Index(rawText, "docs/Guide.md");

        var nodes = result.CodeNodes;

        Assert.Collection(
            nodes,
            node =>
            {
                Assert.Equal("code:doc:docs/Guide.md#document-root", node.CanonicalId);
                Assert.Null(node.ProjectId);
                Assert.Equal(NodeType.Document, node.NodeType);
                Assert.Equal("docs/Guide.md#document-root", node.FullyQualifiedName);
                Assert.Equal("Guide", node.DisplayName);
                Assert.Equal("docs/Guide.md", node.RelativeFilePath);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(1, node.EndLine);
                Assert.Contains("Intro paragraph.", node.Summary, StringComparison.Ordinal);
            },
            node =>
            {
                Assert.Equal("code:doc:docs/Guide.md#overview", node.CanonicalId);
                Assert.Equal("docs/Guide.md#overview", node.FullyQualifiedName);
                Assert.Equal("Guide#overview", node.DisplayName);
                Assert.Equal(3, node.StartLine);
                Assert.Equal(4, node.EndLine);
                Assert.Contains("Overview body.", node.Summary, StringComparison.Ordinal);
            },
            node =>
            {
                Assert.Equal("code:doc:docs/Guide.md#details", node.CanonicalId);
                Assert.Equal("docs/Guide.md#details", node.FullyQualifiedName);
                Assert.Equal("Guide#details", node.DisplayName);
                Assert.Equal(6, node.StartLine);
                Assert.Equal(7, node.EndLine);
                Assert.Contains("Detail body.", node.Summary, StringComparison.Ordinal);
            },
            node =>
            {
                Assert.Equal("code:doc:docs/Guide.md#overview-l9", node.CanonicalId);
                Assert.Equal("docs/Guide.md#overview-l9", node.FullyQualifiedName);
                Assert.Equal("Guide#overview-l9", node.DisplayName);
                Assert.Equal(9, node.StartLine);
                Assert.Equal(10, node.EndLine);
                Assert.Contains("Repeated body.", node.Summary, StringComparison.Ordinal);
            });
        Assert.Collection(
            result.Edges,
            edge =>
            {
                Assert.Equal("code:doc:docs/Guide.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:docs/Guide.md#overview", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/Guide.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:docs/Guide.md#overview-l9", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/Guide.md#overview", edge.CallerId);
                Assert.Equal("code:doc:docs/Guide.md#details", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            });
    }

    [Fact]
    public void WhenIndexingMarkdownWithLocalLinks_ThenEmitsDocumentRootLinksAndHierarchyEdges()
    {
        var rawText =
            """
            # Overview
            See [Doc B](./DocB.md), [Details](#details), [Code](./Program.cs), ![Logo](./logo.png), and [Site](https://example.com).

            ## Details
            Detail body.
            """;
        var indexer = new MarkdownIndexer();

        var result = indexer.Index(rawText, "docs/Guide.md");

        Assert.Collection(
            result.CodeNodes,
            node =>
            {
                Assert.Equal("code:doc:docs/Guide.md#document-root", node.CanonicalId);
                Assert.Equal("docs/Guide.md#document-root", node.FullyQualifiedName);
                Assert.Equal("Guide", node.DisplayName);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(1, node.EndLine);
                Assert.Equal(string.Empty, node.Summary);
            },
            node =>
            {
                Assert.Equal("code:doc:docs/Guide.md#overview", node.CanonicalId);
                Assert.Equal("Guide#overview", node.DisplayName);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(2, node.EndLine);
            },
            node =>
            {
                Assert.Equal("code:doc:docs/Guide.md#details", node.CanonicalId);
                Assert.Equal("Guide#details", node.DisplayName);
                Assert.Equal(4, node.StartLine);
                Assert.Equal(5, node.EndLine);
            });
        Assert.Collection(
            result.Edges,
            edge =>
            {
                Assert.Equal("code:doc:docs/Guide.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:docs/Guide.md#overview", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/Guide.md#overview", edge.CallerId);
                Assert.Equal("code:doc:docs/DocB.md#document-root", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentLink, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/Guide.md#overview", edge.CallerId);
                Assert.Equal("code:doc:docs/Guide.md#details", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentLink, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/Guide.md#overview", edge.CallerId);
                Assert.Equal("code:doc:docs/Guide.md#details", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            });
    }

    [Fact]
    public void WhenIndexingWhitespaceDocument_ThenEmitsDocumentRoot()
    {
        var indexer = new MarkdownIndexer();

        var result = indexer.Index("   \n", "docs/Empty.md");

        Assert.Collection(
            result.CodeNodes,
            node =>
            {
                Assert.Equal("code:doc:docs/Empty.md#document-root", node.CanonicalId);
                Assert.Equal("docs/Empty.md#document-root", node.FullyQualifiedName);
                Assert.Equal("Empty", node.DisplayName);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(1, node.EndLine);
                Assert.Equal(string.Empty, node.Summary);
            });
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void WhenIndexingWikiRootWithObsidianLinks_ThenEmitsWikiRootRelativeDocumentLinks()
    {
        var rawText =
            """
            # Wiki Home
            See [[extractors/markdown]], [[persistence/sqlite-schema#target-overwrites]], and [[persistence/sqlite-schema|schema]].
            """;
        var indexer = new MarkdownIndexer();

        var result = indexer.Index(rawText, "docs/wiki/index.md");

        Assert.Collection(
            result.CodeNodes,
            node =>
            {
                Assert.Equal("code:doc:docs/wiki/index.md#document-root", node.CanonicalId);
                Assert.Equal("docs/wiki/index.md#document-root", node.FullyQualifiedName);
                Assert.Equal("index", node.DisplayName);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(1, node.EndLine);
            },
            node =>
            {
                Assert.Equal("code:doc:docs/wiki/index.md#wiki-home", node.CanonicalId);
                Assert.Equal("docs/wiki/index.md#wiki-home", node.FullyQualifiedName);
                Assert.Equal("index#wiki-home", node.DisplayName);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(2, node.EndLine);
            });
        Assert.Collection(
            result.Edges,
            edge =>
            {
                Assert.Equal("code:doc:docs/wiki/index.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:docs/wiki/index.md#wiki-home", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/wiki/index.md#wiki-home", edge.CallerId);
                Assert.Equal("code:doc:docs/wiki/extractors/markdown.md#document-root", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentLink, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/wiki/index.md#wiki-home", edge.CallerId);
                Assert.Equal("code:doc:docs/wiki/persistence/sqlite-schema.md#document-root", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentLink, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/wiki/index.md#wiki-home", edge.CallerId);
                Assert.Equal("code:doc:docs/wiki/persistence/sqlite-schema.md#target-overwrites", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentLink, edge.EdgeType);
            });
    }

    [Fact]
    public void WhenIndexingNestedWikiPageWithObsidianLinks_ThenResolvesPlainTargetsFromWikiRoot()
    {
        var rawText =
            """
            # Incremental Watch
            See [[extractors/markdown]], [[persistence/sqlite-schema#target-overwrites|schema]], and [[#local-notes]].

            ## Local Notes
            Watch details.
            """;
        var indexer = new MarkdownIndexer();

        var result = indexer.Index(rawText, "docs/wiki/architecture/incremental-watch.md");

        Assert.Collection(
            result.CodeNodes,
            node =>
            {
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#document-root", node.CanonicalId);
                Assert.Equal("architecture/incremental-watch", node.DisplayName);
            },
            node =>
            {
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch", node.CanonicalId);
                Assert.Equal("architecture/incremental-watch#incremental-watch", node.DisplayName);
            },
            node =>
            {
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#local-notes", node.CanonicalId);
                Assert.Equal("architecture/incremental-watch#local-notes", node.DisplayName);
            });
        Assert.Collection(
            result.Edges,
            edge =>
            {
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch", edge.CallerId);
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#local-notes", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentLink, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch", edge.CallerId);
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#local-notes", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch", edge.CallerId);
                Assert.Equal("code:doc:docs/wiki/extractors/markdown.md#document-root", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentLink, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch", edge.CallerId);
                Assert.Equal("code:doc:docs/wiki/persistence/sqlite-schema.md#target-overwrites", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentLink, edge.EdgeType);
            });
    }
}
