using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class DocumentDiscovererTests
{
    [Fact]
    public async Task WhenDiscoveringTargetDocuments_ThenAnchorsGlobsToTargetDirectoryAndEmitsDocumentEdges()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var targetDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var targetPath = Path.Combine(targetDirectory, "Sample.sln");
        var targetDocumentPath = Path.Combine(targetDirectory, "docs", "nested", "Guide.md");
        var linkedDocumentPath = Path.Combine(targetDirectory, "docs", "nested", "Reference.md");
        var outsideDocumentPath = Path.Combine(repositoryRoot, "docs", "nested", "Outside.md");

        Directory.CreateDirectory(Path.GetDirectoryName(targetDocumentPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideDocumentPath)!);
        File.WriteAllText(targetPath, string.Empty);
        File.WriteAllText(
            targetDocumentPath,
            """
            See [Reference](./Reference.md).
            """);
        File.WriteAllText(
            linkedDocumentPath,
            """
            Reference content lives here.
            """);
        File.WriteAllText(
            outsideDocumentPath,
            """
            # Outside Guide
            Should not match.
            """);

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
            var discoverer = new DocumentDiscoverer(
                workspace,
                CreateConfigMonitor("docs/**/*.md"),
                new WorkspaceFileDiscoverer(workspace),
                new MarkdownIndexer());

            var result = await discoverer.Discover(targetPath, TestContext.Current.CancellationToken);

            Assert.Collection(
                result.CodeNodes,
                node =>
                {
                    Assert.Equal("code:doc:src/Sample/docs/nested/Guide.md#document-root", node.CanonicalId);
                    Assert.Equal("src/Sample/docs/nested/Guide.md", node.RelativeFilePath);
                },
                node =>
                {
                    Assert.Equal("code:doc:src/Sample/docs/nested/Reference.md#document-root", node.CanonicalId);
                    Assert.Equal("src/Sample/docs/nested/Reference.md", node.RelativeFilePath);
                });
            Assert.Collection(
                result.Edges,
                edge =>
                {
                    Assert.Equal("code:doc:src/Sample/docs/nested/Guide.md#document-root", edge.CallerId);
                    Assert.Equal("code:doc:src/Sample/docs/nested/Reference.md#document-root", edge.CalleeId);
                    Assert.Equal(Domain.KnowledgeGraph.Enums.EdgeType.DocumentLink, edge.EdgeType);
                });
            Assert.DoesNotContain(
                result.CodeNodes,
                static candidate => candidate.CanonicalId.Contains("Outside", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    [Fact]
    public async Task WhenDiscoveringExplicitFiles_ThenHonorsTargetIncludes()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var targetDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var targetPath = Path.Combine(targetDirectory, "Sample.sln");
        var includedDocumentPath = Path.Combine(targetDirectory, "docs", "Guide.md");
        var excludedDocumentPath = Path.Combine(targetDirectory, "notes", "Outside.md");

        Directory.CreateDirectory(Path.GetDirectoryName(includedDocumentPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(excludedDocumentPath)!);
        File.WriteAllText(targetPath, string.Empty);
        File.WriteAllText(
            includedDocumentPath,
            """
            # Guide
            Included.
            """);
        File.WriteAllText(
            excludedDocumentPath,
            """
            # Outside
            Excluded.
            """);

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
            var discoverer = new DocumentDiscoverer(
                workspace,
                CreateConfigMonitor("docs/**/*.md"),
                new WorkspaceFileDiscoverer(workspace),
                new MarkdownIndexer());

            var result = await discoverer.DiscoverFiles(
                targetPath,
                [includedDocumentPath, excludedDocumentPath],
                TestContext.Current.CancellationToken);

            Assert.NotEmpty(result.CodeNodes);
            Assert.All(
                result.CodeNodes,
                node => Assert.Equal("src/Sample/docs/Guide.md", node.RelativeFilePath));
            Assert.Contains(
                result.CodeNodes,
                static node => node.CanonicalId == "code:doc:src/Sample/docs/Guide.md#document-root");
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    private static IOptionsMonitor<SharpSenseConfig> CreateConfigMonitor(params string[] includePaths)
    {
        var configMonitor = new Mock<IOptionsMonitor<SharpSenseConfig>>(MockBehavior.Strict);
        configMonitor.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new SharpSenseConfig
            {
                IncludePaths = includePaths
            });
        return configMonitor.Object;
    }

    private static string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"sharp-sense-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, ".git"));
        return repositoryRoot;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
