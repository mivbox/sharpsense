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

        var matchQuery = await provider.GetMatchQuery(
            new HybridSearchQuery("Message Get", Limit: 10),
            TestContext.Current.CancellationToken);

        matchQuery.Should().Be("\"Message\"* OR \"Get\"*");
    }

    [Fact]
    public async Task WhenQueryTokenIsShorterThanMinimum_ThenItIsSkipped()
    {
        var provider = new SqliteKeywordCandidateProvider();

        var matchQuery = await provider.GetMatchQuery(
            new HybridSearchQuery("a", Limit: 10),
            TestContext.Current.CancellationToken);

        matchQuery.Should().Be(string.Empty);
    }

    [Fact]
    public async Task WhenQueryHasNoTokens_ThenReturnsEmpty()
    {
        var provider = new SqliteKeywordCandidateProvider();

        var matchQuery = await provider.GetMatchQuery(
            new HybridSearchQuery("...", Limit: 10),
            TestContext.Current.CancellationToken);

        matchQuery.Should().Be(string.Empty);
    }

    [Fact]
    public async Task WhenMatchQueryIsExpanded_ThenHybridSearcherFindsThePrefixHit()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.Seed(context);

        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings.Setup(candidate => candidate.Generate("Mess", TestContext.Current.CancellationToken))
            .ReturnsAsync(new TextEmbedding("Mess", [1f, 0f]));
        var searcher = new HybridSearcher(
            inMemoryFactory.CreateDbContextFactory(),
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(
            new HybridSearchQuery("Mess", Limit: 10),
            TestContext.Current.CancellationToken);

        result.Hits.Should().NotBeEmpty();
        result.Hits.Select(static hit => hit.Id).Should().Contain(KnowledgeGraphFixture.TargetNodeId);
    }

    [Theory]
    [InlineData("*Message")]
    [InlineData("*.cs")]
    [InlineData("Example.Message*")]
    [InlineData("http://localhost:*")]
    [InlineData("\"Message")]
    [InlineData("C#")]
    [InlineData("null?")]
    [InlineData("http://localhost")]
    [InlineData("What's a message?")]
    public async Task WhenNaturalTextContainsPunctuation_ThenSearchDoesNotInterpretItAsFtsSyntax(string searchText)
    {
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await factory.GetContext(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.Seed(context);
        var embeddings = new Mock<IEmbeddingGenerator>();
        embeddings.Setup(generator => generator.Generate(searchText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextEmbedding(searchText, [1f, 0f]));
        var searcher = new HybridSearcher(
            factory.CreateDbContextFactory(),
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(new HybridSearchQuery(searchText), TestContext.Current.CancellationToken);

        result.Hits.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("*Payment", 2)]
    [InlineData("*.cs", 2)]
    [InlineData("Payment.Service*", 2)]
    [InlineData("DisplayName:Payment", 2)]
    [InlineData("DisplayName:Payment.Service*", 2)]
    [InlineData("Payment NOT Other", 3)]
    [InlineData("Payment AND Unused", 3)]
    [InlineData("NEAR(Payment Other)", 2)]
    [InlineData("\"Payment", 2)]
    [InlineData("\"Payment audit\"", 2)]
    [InlineData("Payment OR", 2)]
    [InlineData("http://localhost:*", 0)]
    [InlineData("DisplayName:", 0)]
    public async Task WhenInputResemblesFtsSyntax_ThenSqliteSearchesPlainTextTerms(string searchText, int expectedMatches)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE VIRTUAL TABLE SearchFixture USING fts5(DisplayName, FullyQualifiedName, SearchText, RelativeFilePath);
            INSERT INTO SearchFixture VALUES ('Payment', 'Example.Payment', 'charge card', 'src/Payment.cs');
            INSERT INTO SearchFixture VALUES ('Other', 'Example.Other', 'Payment audit', 'src/Other.cs');
            INSERT INTO SearchFixture VALUES ('Unused', 'Example.Unused', 'nothing', 'src/Unused.ts');
            """;
        await command.ExecuteNonQueryAsync(ct);

        var query = await new SqliteKeywordCandidateProvider().GetMatchQuery(new(searchText), ct);
        command.CommandText = "SELECT COUNT(*) FROM SearchFixture WHERE SearchFixture MATCH $query";
        command.Parameters.AddWithValue("$query", query);

        Convert.ToInt32(await command.ExecuteScalarAsync(ct)).Should().Be(expectedMatches);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("***")]
    [InlineData(":")]
    [InlineData("\"\"")]
    [InlineData("()[]:*")]
    public async Task WhenOnlyPunctuationRemains_ThenSearchReturnsNoHitsWithoutGeneratingAnEmbedding(string searchText)
    {
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        var searcher = new HybridSearcher(factory.CreateDbContextFactory(), embeddings.Object, new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(new HybridSearchQuery(searchText), TestContext.Current.CancellationToken);

        result.SearchText.Should().Be(searchText);
        result.Hits.Should().BeEmpty();
        embeddings.VerifyNoOtherCalls();
    }
}
