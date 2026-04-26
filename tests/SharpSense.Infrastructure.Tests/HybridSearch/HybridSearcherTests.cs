using Moq;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.HybridSearch;

public sealed class HybridSearcherTests
{
    [Fact]
    public async Task WhenSearchHasSemanticCandidates_ThenRanksClosestMatchFirst()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.SeedAsync(context);
        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings.Setup(candidate => candidate.Generate("Message", TestContext.Current.CancellationToken))
            .ReturnsAsync(new TextEmbedding("Message", [1f, 0f]));
        var searcher = new HybridSearcher(
            inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>(),
            embeddings.Object);

        var result = await searcher.Search(
            new HybridSearchQuery("Message", Limit: 3),
            TestContext.Current.CancellationToken);

        Assert.Equal("Message", result.SearchText);
        Assert.NotEmpty(result.Hits);
        Assert.Equal(KnowledgeGraphFixture.TargetNodeId, result.Hits[0].Id);
        embeddings.Verify(candidate => candidate.Generate("Message", TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task WhenSearchUsesProjectAndNodeTypeFilters_ThenReturnsOnlyMatchingNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.SeedAsync(context);
        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings.Setup(candidate => candidate.Generate("Message", TestContext.Current.CancellationToken))
            .ReturnsAsync(new TextEmbedding("Message", [1f, 0f]));
        var searcher = new HybridSearcher(
            inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>(),
            embeddings.Object);

        var result = await searcher.Search(
            new HybridSearchQuery(
                "Message",
                Limit: 10,
                ProjectId: KnowledgeGraphFixture.AppProjectId,
                IncludedNodeTypes: [NodeType.Method]),
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Hits);
        Assert.All(result.Hits, static hit => Assert.Equal(NodeType.Method, hit.NodeType));
        Assert.All(result.Hits, static hit => Assert.Equal(KnowledgeGraphFixture.AppProjectId, hit.ProjectId));
        Assert.DoesNotContain(result.Hits, static hit => hit.Id == KnowledgeGraphFixture.MessageNodeId);
    }
}
