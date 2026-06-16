using AwesomeAssertions;
using Moq;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.HybridSearch;

public sealed class SqliteKeywordCandidateProviderTests
{
    [Fact]
    public async Task WhenQueryIsBareText_ThenItExpandsTokensAsFtsPrefixes()
    {
        var provider = new SqliteKeywordCandidateProvider();

        var matchQuery = await provider.GetMatchQueryAsync(
            new HybridSearchQuery("Message Get", Limit: 10),
            TestContext.Current.CancellationToken);

        matchQuery.Should().Be("Message* OR Get*");
    }

    [Fact]
    public async Task WhenQueryTokenIsShorterThanMinimum_ThenItIsSkipped()
    {
        var provider = new SqliteKeywordCandidateProvider();

        var matchQuery = await provider.GetMatchQueryAsync(
            new HybridSearchQuery("a", Limit: 10),
            TestContext.Current.CancellationToken);

        matchQuery.Should().Be(string.Empty);
    }

    [Fact]
    public async Task WhenQueryHasNoTokens_ThenReturnsEmpty()
    {
        var provider = new SqliteKeywordCandidateProvider();

        var matchQuery = await provider.GetMatchQueryAsync(
            new HybridSearchQuery("...", Limit: 10),
            TestContext.Current.CancellationToken);

        matchQuery.Should().Be(string.Empty);
    }

    [Fact]
    public async Task WhenQueryContainsStandardFtsOperators_ThenItIsForwardedVerbatim()
    {
        var provider = new SqliteKeywordCandidateProvider();

        var matchQuery = await provider.GetMatchQueryAsync(
            new HybridSearchQuery("FullyQualifiedName:Message", Limit: 10),
            TestContext.Current.CancellationToken);

        matchQuery.Should().Be("FullyQualifiedName:Message");
    }

    [Fact]
    public async Task WhenMatchQueryIsExpanded_ThenHybridSearcherFindsThePrefixHit()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.SeedAsync(context);

        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings.Setup(candidate => candidate.Generate("Mess", TestContext.Current.CancellationToken))
            .ReturnsAsync(new TextEmbedding("Mess", [1f, 0f]));
        var searcher = new HybridSearcher(
            inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>(),
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(
            new HybridSearchQuery("Mess", Limit: 10),
            TestContext.Current.CancellationToken);

        result.Hits.Should().NotBeEmpty();
        result.Hits.Select(static hit => hit.Id).Should().Contain(KnowledgeGraphFixture.TargetNodeId);
    }
}
