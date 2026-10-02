using AwesomeAssertions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing.Markdown;

namespace SharpSense.Infrastructure.Tests.Indexing.Markdown;

public sealed class MarkdownIndexerTests
{
    [Theory]
    [InlineData("space%20target.md", "space target.md#document-root")]
    [InlineData("caf%C3%A9.md#Details", "café.md#details")]
    [InlineData("hash%23target.md", "hash#target.md#document-root")]
    [InlineData("query%3Ftarget.md", "query?target.md#document-root")]
    [InlineData("literal%2520target.md", "literal%20target.md#document-root")]
    public void WhenLocalLinkHasEncodedPath_ThenItResolvesDecodedDocument(string target, string expectedTarget)
    {
        var indexer = new MarkdownIndexer();

        var result = indexer.Index($"[Target]({target})", "docs/Guide.md");

        result.Edges.Should().ContainSingle()
            .Which.CalleeId.Should().Be($"code:doc:docs/{expectedTarget}");
    }

    [Theory]
    [InlineData("```md\n[[Target#ignored]]\n```")]
    [InlineData("    [[Target#ignored]]")]
    [InlineData("> ```md\n> [[Target#ignored]]\n> ```")]
    [InlineData("- Example:\n\n      [[Target#ignored]]")]
    public void WhenWikiLinksAppearInCodeBlocks_ThenOnlyProseCreatesDependencies(string example)
    {
        var indexer = new MarkdownIndexer();

        var result = indexer.Index($"{example}\n\n[[Target#real]]", "docs/wiki/Guide.md");

        result.Edges.Where(edge => edge.EdgeType == EdgeType.DocumentLink)
            .Select(edge => edge.CalleeId).Should().Equal("code:doc:docs/wiki/Target.md#real");
        result.CodeNodes.Should().Contain(node => node.Summary.Contains("Target#ignored"));
    }

    [Theory]
    [InlineData("bad%00.md")]
    [InlineData("folder%2Fbad%00.md")]
    public void WhenLocalLinkDecodesToInvalidPath_ThenKeepsDocumentAndValidLinks(string target)
    {
        var indexer = new MarkdownIndexer();

        var result = indexer.Index($"# Guide\n[Bad]({target}) and [Good](Valid.md)", "docs/Guide.md");

        result.CodeNodes.Should().Contain(node => node.Summary.Contains("Bad"));
        result.Edges.Where(edge => edge.EdgeType == EdgeType.DocumentLink)
            .Select(edge => edge.CalleeId).Should().Equal("code:doc:docs/Valid.md#document-root");
    }

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

        nodes.Should().SatisfyRespectively(
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/Guide.md#document-root");
                node.ProjectId.Should().BeNull();
                node.NodeType.Should().Be(NodeType.Document);
                node.FullyQualifiedName.Should().Be("docs/Guide.md#document-root");
                node.DisplayName.Should().Be("Guide");
                node.RelativeFilePath.Should().Be("docs/Guide.md");
                node.StartLine.Should().Be(1);
                node.EndLine.Should().Be(1);
                node.Summary.Should().Contain("Intro paragraph.");
            },
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/Guide.md#overview");
                node.FullyQualifiedName.Should().Be("docs/Guide.md#overview");
                node.DisplayName.Should().Be("Guide#overview");
                node.StartLine.Should().Be(3);
                node.EndLine.Should().Be(4);
                node.Summary.Should().Contain("Overview body.");
            },
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/Guide.md#details");
                node.FullyQualifiedName.Should().Be("docs/Guide.md#details");
                node.DisplayName.Should().Be("Guide#details");
                node.StartLine.Should().Be(6);
                node.EndLine.Should().Be(7);
                node.Summary.Should().Contain("Detail body.");
            },
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/Guide.md#overview-l9");
                node.FullyQualifiedName.Should().Be("docs/Guide.md#overview-l9");
                node.DisplayName.Should().Be("Guide#overview-l9");
                node.StartLine.Should().Be(9);
                node.EndLine.Should().Be(10);
                node.Summary.Should().Contain("Repeated body.");
            });
        result.Edges.Count.Should().Be(3);
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/Guide.md#document-root",
                CalleeId: "code:doc:docs/Guide.md#overview",
                EdgeType: EdgeType.ParentOf
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/Guide.md#document-root",
                CalleeId: "code:doc:docs/Guide.md#overview-l9",
                EdgeType: EdgeType.ParentOf
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/Guide.md#overview",
                CalleeId: "code:doc:docs/Guide.md#details",
                EdgeType: EdgeType.ParentOf
            }).Should().NotBeEmpty();
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

        result.CodeNodes.Should().SatisfyRespectively(
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/Guide.md#document-root");
                node.FullyQualifiedName.Should().Be("docs/Guide.md#document-root");
                node.DisplayName.Should().Be("Guide");
                node.StartLine.Should().Be(1);
                node.EndLine.Should().Be(1);
                node.Summary.Should().Be(string.Empty);
            },
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/Guide.md#overview");
                node.DisplayName.Should().Be("Guide#overview");
                node.StartLine.Should().Be(1);
                node.EndLine.Should().Be(2);
            },
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/Guide.md#details");
                node.DisplayName.Should().Be("Guide#details");
                node.StartLine.Should().Be(4);
                node.EndLine.Should().Be(5);
            });
        result.Edges.Count.Should().Be(4);
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/Guide.md#document-root",
                CalleeId: "code:doc:docs/Guide.md#overview",
                EdgeType: EdgeType.ParentOf
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/Guide.md#overview",
                CalleeId: "code:doc:docs/DocB.md#document-root",
                EdgeType: EdgeType.DocumentLink
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/Guide.md#overview",
                CalleeId: "code:doc:docs/Guide.md#details",
                EdgeType: EdgeType.DocumentLink
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/Guide.md#overview",
                CalleeId: "code:doc:docs/Guide.md#details",
                EdgeType: EdgeType.ParentOf
            }).Should().NotBeEmpty();
    }

    [Fact]
    public void WhenIndexingWhitespaceDocument_ThenEmitsDocumentRoot()
    {
        var indexer = new MarkdownIndexer();

        var result = indexer.Index("   \n", "docs/Empty.md");

        result.CodeNodes.Should().SatisfyRespectively(node =>
        {
            node.CanonicalId.Should().Be("code:doc:docs/Empty.md#document-root");
            node.FullyQualifiedName.Should().Be("docs/Empty.md#document-root");
            node.DisplayName.Should().Be("Empty");
            node.StartLine.Should().Be(1);
            node.EndLine.Should().Be(1);
            node.Summary.Should().Be(string.Empty);
        });
        result.Edges.Should().BeEmpty();
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

        result.CodeNodes.Should().SatisfyRespectively(
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/wiki/index.md#document-root");
                node.FullyQualifiedName.Should().Be("docs/wiki/index.md#document-root");
                node.DisplayName.Should().Be("index");
                node.StartLine.Should().Be(1);
                node.EndLine.Should().Be(1);
            },
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/wiki/index.md#wiki-home");
                node.FullyQualifiedName.Should().Be("docs/wiki/index.md#wiki-home");
                node.DisplayName.Should().Be("index#wiki-home");
                node.StartLine.Should().Be(1);
                node.EndLine.Should().Be(2);
            });
        result.Edges.Count.Should().Be(4);
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/wiki/index.md#document-root",
                CalleeId: "code:doc:docs/wiki/index.md#wiki-home",
                EdgeType: EdgeType.ParentOf
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/wiki/index.md#wiki-home",
                CalleeId: "code:doc:docs/wiki/extractors/markdown.md#document-root",
                EdgeType: EdgeType.DocumentLink
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/wiki/index.md#wiki-home",
                CalleeId: "code:doc:docs/wiki/persistence/sqlite-schema.md#document-root",
                EdgeType: EdgeType.DocumentLink
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/wiki/index.md#wiki-home",
                CalleeId: "code:doc:docs/wiki/persistence/sqlite-schema.md#target-overwrites",
                EdgeType: EdgeType.DocumentLink
            }).Should().NotBeEmpty();
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

        result.CodeNodes.Should().SatisfyRespectively(
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/wiki/architecture/incremental-watch.md#document-root");
                node.DisplayName.Should().Be("architecture/incremental-watch");
            },
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch");
                node.DisplayName.Should().Be("architecture/incremental-watch#incremental-watch");
            },
            node =>
            {
                node.CanonicalId.Should().Be("code:doc:docs/wiki/architecture/incremental-watch.md#local-notes");
                node.DisplayName.Should().Be("architecture/incremental-watch#local-notes");
            });
        result.Edges.Count.Should().Be(5);
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/wiki/architecture/incremental-watch.md#document-root",
                CalleeId: "code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch",
                EdgeType: EdgeType.ParentOf
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch",
                CalleeId: "code:doc:docs/wiki/architecture/incremental-watch.md#local-notes",
                EdgeType: EdgeType.DocumentLink
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch",
                CalleeId: "code:doc:docs/wiki/architecture/incremental-watch.md#local-notes",
                EdgeType: EdgeType.ParentOf
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch",
                CalleeId: "code:doc:docs/wiki/extractors/markdown.md#document-root",
                EdgeType: EdgeType.DocumentLink
            }).Should().NotBeEmpty();
        result.Edges
            .Where(static edge => edge is
            {
                CallerId: "code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch",
                CalleeId: "code:doc:docs/wiki/persistence/sqlite-schema.md#target-overwrites",
                EdgeType: EdgeType.DocumentLink
            }).Should().NotBeEmpty();
    }
}
