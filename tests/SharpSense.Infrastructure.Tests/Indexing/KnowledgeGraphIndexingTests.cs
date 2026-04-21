using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Features.Indexing.IndexSolution;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Indexing;

[Collection("MSBuild workspace")]
public sealed class KnowledgeGraphIndexingTests
{
    [Fact]
    public async Task WhenIndexingSolutionWithDocumentConfig_ThenPersistsDiscoveredDocumentNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var repositoryRoot = GetRepositoryRoot();
        var solutionPath = GetFixturePath("CommandPipelineFixture.sln");
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
        var indexing = new KnowledgeGraphIndexing(
            new ILanguageExtractor[]
            {
                new CSharpLanguageExtractor(new RoslynSolutionAnalysisEngine(), workspace),
                new MarkdownDocumentExtractor(new DocumentDiscoverer(workspace, new MarkdownIndexer()))
            },
            new NoOpEmbeddingGenerator(),
            context,
            workspace);

        var summary = await indexing.Index(
            new IndexSolutionCommand(solutionPath, IncludeEmbeddings: false),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var documentNodes = await context.CodeNodes
            .AsNoTracking()
            .Where((CodeNode codeNode) => codeNode.NodeType == NodeType.Document)
            .OrderBy(codeNode => codeNode.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var documentEdges = await context.DependencyEdges
            .AsNoTracking()
            .Where(edge => edge.EdgeType == EdgeType.DocumentLink)
            .OrderBy(edge => edge.CallerId)
            .ThenBy(edge => edge.CalleeId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var hierarchyEdges = await context.DependencyEdges
            .AsNoTracking()
            .Where(edge => edge.EdgeType == EdgeType.DocumentHierarchy)
            .OrderBy(edge => edge.CallerId)
            .ThenBy(edge => edge.CalleeId)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(solutionPath, summary.SolutionPath);
        Assert.Equal(5, documentNodes.Length);
        Assert.Collection(
            documentNodes,
            node =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocA.md#document-root", node.Id);
                Assert.Null(node.ProjectId);
                Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocA.md", node.RelativeFilePath);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(1, node.EndLine);
                Assert.Contains("[DocB](./DocB.md)", node.Summary, StringComparison.Ordinal);
            },
            node =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocB.md#document-root", node.Id);
                Assert.Null(node.ProjectId);
                Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocB.md", node.RelativeFilePath);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(1, node.EndLine);
                Assert.Contains("Doc B reference content.", node.Summary, StringComparison.Ordinal);
            },
            node =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#document-root", node.Id);
                Assert.Null(node.ProjectId);
                Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md", node.RelativeFilePath);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(1, node.EndLine);
                Assert.Equal(string.Empty, node.Summary);
            },
            node =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#getting-started", node.Id);
                Assert.Null(node.ProjectId);
                Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md", node.RelativeFilePath);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(2, node.EndLine);
                Assert.Contains("pipeline fixture documentation", node.Summary, StringComparison.Ordinal);
            },
            node =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#getting-started-l4", node.Id);
                Assert.Null(node.ProjectId);
                Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md", node.RelativeFilePath);
                Assert.Equal(4, node.StartLine);
                Assert.Equal(5, node.EndLine);
                Assert.Contains("repeated heading", node.Summary, StringComparison.Ordinal);
            });
        Assert.Collection(
            documentEdges,
            edge =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocA.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocB.md#document-root", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentLink, edge.EdgeType);
            });
        Assert.Collection(
            hierarchyEdges,
            edge =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#getting-started", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#getting-started-l4", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            });
        Assert.True(summary.CodeNodeCount >= documentNodes.Length);
    }

    private static string GetFixturePath(string relativePath)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture",
            relativePath));

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private sealed class NoOpEmbeddingGenerator : IEmbeddingGenerator
    {
        public Task<TextEmbedding> Generate(string text, CancellationToken ct = default)
            => Task.FromResult(new TextEmbedding(text, []));

        public Task<IReadOnlyList<TextEmbedding>> GenerateBatch(
            IEnumerable<string> texts,
            IProgress<EmbeddingGenerationProgress>? progress,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TextEmbedding>>([]);
    }
}
