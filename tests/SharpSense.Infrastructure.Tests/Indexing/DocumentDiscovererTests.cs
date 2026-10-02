using AwesomeAssertions;
using Moq;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class DocumentDiscovererTests
{
    [Fact]
    public async Task WhenWorkspacePatternsAreSelected_ThenDiscoveryUsesRepositoryRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        var workspace = CreateRepositoryWorkspace("/repo", "/repo");
        var discoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        string[] patterns = ["docs/**/*.md", "README.md"];
        discoverer
            .Setup(candidate => candidate.GetAllowedFiles("/repo", patterns, ct))
            .ReturnsAsync([]);
        var subject = new DocumentDiscoverer(
            workspace.Object,
            discoverer.Object,
            new Mock<IMarkdownIndexer>(MockBehavior.Strict).Object,
            new MockFileSystem());

        var result = await subject.Discover("/repo", ct, patterns);

        result.CodeNodes.Should().BeEmpty();
        discoverer.Verify(
            candidate => candidate.GetAllowedFiles("/repo", patterns, ct),
            Times.Once);
    }

    [Fact]
    public async Task WhenDiscoveringTargetDocuments_ThenAnchorsGlobsToTargetDirectoryAndEmitsDocumentEdges()
    {
        var ct = TestContext.Current.CancellationToken;
        const string targetPath = "/repo/src/Sample/Sample.sln";
        const string targetDirectory = "/repo/src/Sample";
        const string guidePath = "/repo/src/Sample/docs/nested/Guide.md";
        const string referencePath = "/repo/src/Sample/docs/nested/Reference.md";
        const string guideRelativePath = "src/Sample/docs/nested/Guide.md";
        const string referenceRelativePath = "src/Sample/docs/nested/Reference.md";
        var includePaths = new[] { "docs/**/*.md" };
        var repositoryWorkspace = CreateRepositoryWorkspace(targetPath, targetDirectory);
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [guidePath] = new("See [Reference](./Reference.md)."),
            [referencePath] = new("Reference content lives here.")
        });
        var fileDiscoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        var markdownIndexer = new Mock<IMarkdownIndexer>(MockBehavior.Strict);
        var discoverer = new DocumentDiscoverer(
            repositoryWorkspace.Object,
            fileDiscoverer.Object,
            markdownIndexer.Object,
            fileSystem);

        fileDiscoverer
            .Setup(candidate => candidate.GetAllowedFiles(
                targetDirectory,
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(includePaths)),
                ct))
            .ReturnsAsync(
                [
                    new DiscoveredFile(guidePath, guideRelativePath),
                    new DiscoveredFile(referencePath, referenceRelativePath)
                ]);
        markdownIndexer
            .Setup(candidate => candidate.Index("See [Reference](./Reference.md).", guideRelativePath))
            .Returns(new MarkdownIndexResult(
                [
                    CreateDocumentNode("code:doc:src/Sample/docs/nested/Guide.md#document-root", guideRelativePath)
                ],
                [
                    CreateEdge(
                        "code:doc:src/Sample/docs/nested/Guide.md#document-root",
                        "code:doc:src/Sample/docs/nested/Reference.md#document-root",
                        EdgeType.DocumentLink)
                ]));
        markdownIndexer
            .Setup(candidate => candidate.Index("Reference content lives here.", referenceRelativePath))
            .Returns(new MarkdownIndexResult(
                [
                    CreateDocumentNode("code:doc:src/Sample/docs/nested/Reference.md#document-root", referenceRelativePath)
                ],
                []));

        var result = await discoverer.Discover(targetPath, ct, includePaths);

        result.CodeNodes
            .Select(static node => node.CanonicalId)
            .Should()
            .Equal(
                "code:doc:src/Sample/docs/nested/Guide.md#document-root",
                "code:doc:src/Sample/docs/nested/Reference.md#document-root");
        result.CodeNodes
            .Select(static node => node.RelativeFilePath)
            .Should()
            .Equal(guideRelativePath, referenceRelativePath);
        result.Edges.Should().ContainSingle();
        result.Edges[0].CallerId.Should().Be("code:doc:src/Sample/docs/nested/Guide.md#document-root");
        result.Edges[0].CalleeId.Should().Be("code:doc:src/Sample/docs/nested/Reference.md#document-root");
        result.Edges[0].EdgeType.Should().Be(EdgeType.DocumentLink);
    }

    [Fact]
    public async Task WhenTargetHasFilesOutsideItsIncludes_ThenIndexesOnlyMatchingDocuments()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/src/Sample/Sample.sln"] = new(""),
            ["/repo/src/Sample/docs/Guide.md"] = new("# Guide"),
            ["/repo/src/Sample/Outside.md"] = new("# Outside"),
            ["/repo/docs/Other.md"] = new("# Other target"),
            ["/repo/src/Sample/docs/Code.cs"] = new("public class Code {}")
        });
        var workspace = new RepositoryWorkspace("/repo", "/test-storage/index.db", fileSystem);
        var discoverer = new DocumentDiscoverer(
            workspace,
            new WorkspaceFileDiscoverer(workspace, fileSystem),
            new MarkdownIndexer(),
            fileSystem);

        var result = await discoverer.Discover(
            "/repo/src/Sample/Sample.sln",
            TestContext.Current.CancellationToken,
            ["docs/**/*.md"]);

        result.CodeNodes.Should().NotBeEmpty();
        result.CodeNodes.Should().OnlyContain(node => node.RelativeFilePath == "src/Sample/docs/Guide.md");
        result.CodeNodes.Should().Contain(node => node.CanonicalId == "code:doc:src/Sample/docs/Guide.md#document-root");
    }

    private static Mock<IRepositoryWorkspace> CreateRepositoryWorkspace(
        string targetPath,
        string targetDirectory)
    {
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace
            .Setup(candidate => candidate.GetRequiredTargetDirectoryPath(targetPath))
            .Returns(targetDirectory);

        return repositoryWorkspace;
    }

    private static CodeNode CreateDocumentNode(
        string canonicalId,
        string relativeFilePath)
        => new()
        {
            CanonicalId = canonicalId,
            FullyQualifiedName = canonicalId["code:doc:".Length..],
            DisplayName = canonicalId[(canonicalId.LastIndexOf('/') + 1)..],
            NodeType = NodeType.Document,
            RelativeFilePath = relativeFilePath,
            StartLine = 1,
            EndLine = 1,
            Summary = string.Empty
        };

    private static DependencyEdge CreateEdge(
        string callerId,
        string calleeId,
        EdgeType edgeType)
        => new()
        {
            CallerId = callerId,
            CalleeId = calleeId,
            EdgeType = edgeType
        };
}
