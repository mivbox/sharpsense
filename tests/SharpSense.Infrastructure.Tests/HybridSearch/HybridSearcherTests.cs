using AwesomeAssertions;
using Moq;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
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
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(
            new HybridSearchQuery("Message", Limit: 3),
            TestContext.Current.CancellationToken);

        result.SearchText.Should().Be("Message");
        result.Hits.Should().NotBeEmpty();
        result.Hits.Select(static hit => hit.Id).Should().BeEquivalentTo(new[] { 7, 1, 6 });
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
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(
            new HybridSearchQuery(
                "Message",
                Limit: 10,
                ProjectId: KnowledgeGraphFixture.AppProjectId,
                IncludedNodeTypes: [NodeType.Method]),
            TestContext.Current.CancellationToken);

        result.Hits.Should().NotBeEmpty();
        result.Hits.Should().OnlyContain(static hit => hit.NodeType == NodeType.Method);
        result.Hits.Should().OnlyContain(static hit => hit.ProjectId == KnowledgeGraphFixture.AppProjectId);
        result.Hits.Should().NotContain(static hit => hit.Id == KnowledgeGraphFixture.MessageNodeId);
    }

    [Fact]
    public async Task WhenSearchIncludesMemoriesWithMatchingTags_ThenItReturnsMemoryMatchedNode()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.SeedAsync(context);
        context.MemoryNodes.Add(
            new MemoryNodeRecord
            {
                Id = Guid.NewGuid(),
                TargetFullyQualifiedName = KnowledgeGraphFixture.DirectCallerFullyQualifiedName,
                TargetCodeHash = "hash-1",
                Content = "Security review note",
                ContentHash = "memory-hash-1",
                TagsJson = "[\"security\"]",
                VectorEmbedding = [1f, 0f],
                CreatedAt = DateTimeOffset.Parse("2026-05-14T00:00:00+00:00")
            });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings.Setup(candidate => candidate.Generate("security", TestContext.Current.CancellationToken))
            .ReturnsAsync(new TextEmbedding("security", [1f, 0f]));
        var searcher = new HybridSearcher(
            inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>(),
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(
            new HybridSearchQuery(
                "security",
                Limit: 5,
                IncludeMemories: true,
                TagFilters: ["security"]),
            TestContext.Current.CancellationToken);

        result.Hits.Should().NotBeEmpty();
        result.Hits.Select(static hit => hit.Id).Should().Contain(KnowledgeGraphFixture.DirectCallerNodeId);
        embeddings.VerifyAll();
    }
}
