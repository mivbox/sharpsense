using AwesomeAssertions;
using Microsoft.Data.Sqlite;
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

        matchQuery.Should().Be("\"Message\"* OR \"Get\"*");
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

    [Theory]
    [InlineData("C#")]
    [InlineData("null?")]
    [InlineData("http://localhost")]
    [InlineData("What's a message?")]
    public async Task WhenNaturalTextContainsPunctuation_ThenSearchDoesNotInterpretItAsFtsSyntax(string searchText)
    {
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await factory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.SeedAsync(context);
        var embeddings = new Mock<IEmbeddingGenerator>();
        embeddings.Setup(generator => generator.Generate(searchText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextEmbedding(searchText, [1f, 0f]));
        var searcher = new HybridSearcher(factory.CreateDbContextFactory<SharpSenseDbContext>(), embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(new HybridSearchQuery(searchText), TestContext.Current.CancellationToken);

        result.Hits.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("Message OR Provider")]
    [InlineData("NEAR(Message Provider)")]
    [InlineData("anchor NEAR (bar baz)")]
    [InlineData("NEAR\t(Message Provider)")]
    [InlineData("\"Message Provider\"")]
    [InlineData("FullyQualifiedName:Message")]
    [InlineData("-DisplayName:Message")]
    [InlineData("{DisplayName SearchText}:Message")]
    [InlineData("- {DisplayName SearchText} : Message")]
    [InlineData("(-DisplayName:Message)")]
    [InlineData("Message*")]
    public async Task WhenUsingExplicitFtsSyntax_ThenQueryRemainsUnchanged(string searchText)
    {
        var query = await new SqliteKeywordCandidateProvider().GetMatchQueryAsync(new(searchText), TestContext.Current.CancellationToken);
        query.Should().Be(searchText);
    }

    [Theory]
    [InlineData("-DisplayName:Payment", 0)]
    [InlineData("{FullyQualifiedName SearchText}:Payment", 0)]
    [InlineData("{DisplayName SearchText}:Payment", 1)]
    [InlineData("-{FullyQualifiedName SearchText}:Payment", 1)]
    public async Task WhenUsingColumnFilters_ThenSqliteAppliesRequestedScope(string searchText, int expectedMatches)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE VIRTUAL TABLE SearchFixture USING fts5(DisplayName, FullyQualifiedName, SearchText, RelativeFilePath);
            INSERT INTO SearchFixture VALUES ('Payment', 'Example.Service', 'audit', 'src/Service.cs');
            """;
        await command.ExecuteNonQueryAsync(ct);

        var query = await new SqliteKeywordCandidateProvider().GetMatchQueryAsync(new(searchText), ct);
        command.CommandText = "SELECT COUNT(*) FROM SearchFixture WHERE SearchFixture MATCH $query";
        command.Parameters.AddWithValue("$query", query);

        Convert.ToInt32(await command.ExecuteScalarAsync(ct)).Should().Be(expectedMatches);
    }
}
