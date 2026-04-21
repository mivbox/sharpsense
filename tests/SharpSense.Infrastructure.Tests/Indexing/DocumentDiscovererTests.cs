using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class DocumentDiscovererTests
{
    [Fact]
    public async Task WhenDiscoveringSolutionDocuments_ThenAnchorsGlobsToSolutionDirectoryAndEmitsDocumentEdges()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var solutionDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var solutionPath = Path.Combine(solutionDirectory, "Sample.sln");
        var configPath = Path.Combine(solutionDirectory, "sharpsense.yaml");
        var solutionDocumentPath = Path.Combine(solutionDirectory, "docs", "nested", "Guide.md");
        var linkedDocumentPath = Path.Combine(solutionDirectory, "docs", "nested", "Reference.md");
        var outsideDocumentPath = Path.Combine(repositoryRoot, "docs", "nested", "Outside.md");

        Directory.CreateDirectory(Path.GetDirectoryName(solutionDocumentPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideDocumentPath)!);
        File.WriteAllText(solutionPath, string.Empty);
        File.WriteAllText(
            configPath,
            """
            includePaths:
              - docs/**/*.md
            """);
        File.WriteAllText(
            solutionDocumentPath,
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
            var discoverer = new DocumentDiscoverer(workspace, new MarkdownIndexer());

            var result = await discoverer.Discover(solutionPath, TestContext.Current.CancellationToken);

            Assert.Collection(
                result.CodeNodes,
                node =>
                {
                    Assert.Equal("code:doc:src/Sample/docs/nested/Guide.md#document-root", node.Id);
                    Assert.Equal("src/Sample/docs/nested/Guide.md", node.RelativeFilePath);
                },
                node =>
                {
                    Assert.Equal("code:doc:src/Sample/docs/nested/Reference.md#document-root", node.Id);
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
                static candidate => candidate.Id.Contains("Outside", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
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
