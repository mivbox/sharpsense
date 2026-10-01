using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Errors;
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
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhenSearchQueryIsBlank_ThenRejectsItBeforeGeneratingEmbeddings(string expression)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(UseMigrations: true));
        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        var searcher = new HybridSearcher(
            factory.CreateDbContextFactory(),
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(new HybridSearchQuery(expression), ct);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ServiceError>()
            .Which.ErrorCode.Should().Be(ServiceErrorCode.InvalidArgument);
        embeddings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenSearchIndexIsMissing_ThenPreservesDatabaseFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await factory.GetContext(ct);
        await context.Database.ExecuteSqlRawAsync("DROP TABLE CodeNodeSearch", ct);
        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings
            .Setup(candidate => candidate.Generate("Message", ct))
            .ReturnsAsync(new TextEmbedding("Message", [1f, 0f]));
        var searcher = new HybridSearcher(
            factory.CreateDbContextFactory(),
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var action = () => searcher.Search(new HybridSearchQuery("Message"), ct);

        (await action.Should().ThrowExactlyAsync<SqliteException>())
            .Which.Message.Should().Contain("no such table: CodeNodeSearch");
    }

    [Fact]
    public async Task WhenSearchCombinesKeywordAndSemanticCandidates_ThenOrdersHitsByFusedScore()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(context, ct);
        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings
            .Setup(candidate => candidate.Generate("Message", ct))
            .ReturnsAsync(new TextEmbedding("Message", [1f, 0f]));
        var searcher = new HybridSearcher(
            inMemoryFactory.CreateDbContextFactory(),
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(
            new HybridSearchQuery("Message", Limit: 3),
            ct);

        result.Value.SearchText.Should().Be("Message");
        result.Value.Hits.Should().NotBeEmpty();
        result.Value.Hits
            .Select(static hit => hit.Id)
            .Should()
            .Equal(7, 1, 6);
        embeddings.Verify(
            candidate => candidate.Generate("Message", ct),
            Times.Once);
    }

    [Fact]
    public async Task WhenSearchUsesProjectAndNodeTypeFilters_ThenReturnsOnlyMatchingNodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(context, ct);
        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings
            .Setup(candidate => candidate.Generate("Message", ct))
            .ReturnsAsync(new TextEmbedding("Message", [1f, 0f]));
        var searcher = new HybridSearcher(
            inMemoryFactory.CreateDbContextFactory(),
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(
            new HybridSearchQuery(
                "Message",
                Limit: 10,
                ProjectId: KnowledgeGraphFixture.AppProjectId,
                IncludedNodeTypes: [NodeType.Method]),
            ct);

        result.Value.Hits.Should().NotBeEmpty();
        result.Value.Hits.Should().OnlyContain(static hit => hit.NodeType == NodeType.Method);
        result.Value.Hits.Should().OnlyContain(static hit => hit.ProjectId == KnowledgeGraphFixture.AppProjectId);
        result.Value.Hits.Should().NotContain(static hit => hit.Id == KnowledgeGraphFixture.MessageNodeId);
    }

    [Fact]
    public async Task WhenSearchIncludesMemoriesWithMatchingTags_ThenItReturnsMemoryMatchedNode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(context, ct);
        await context.CodeNodes.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.VectorEmbedding, (float[]?)null),
            ct);
        context.MemoryNodes.Add(
            new MemoryNodeRecord
            {
                Id = Guid.NewGuid(),
                TargetCodeNodeId = KnowledgeGraphFixture.DirectCallerNodeId,
                TargetCodeHash = "hash-1",
                Content = "Security review note",
                ContentHash = "memory-hash-1",
                TagsJson = "[\"security\"]",
                CreatedAt = DateTimeOffset.Parse("2026-05-14T00:00:00+00:00")
            });
        context.MemoryNodes.Add(new MemoryNodeRecord
        {
            Id = Guid.NewGuid(),
            TargetCodeNodeId = KnowledgeGraphFixture.TargetNodeId,
            TargetCodeHash = "hash-2",
            Content = "Security review note with a different tag",
            ContentHash = "memory-hash-2",
            TagsJson = "[\"performance\"]",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync(ct);

        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings
            .Setup(candidate => candidate.Generate("security", ct))
            .ReturnsAsync(new TextEmbedding("security", [1f, 0f]));
        var searcher = new HybridSearcher(
            inMemoryFactory.CreateDbContextFactory(),
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(
            new HybridSearchQuery(
                "security",
                Limit: 5,
                IncludeMemories: true,
                TagFilters: ["security"]),
            ct);

        result.Value.Hits
            .Select(static hit => hit.Id)
            .Should()
            .Equal(KnowledgeGraphFixture.DirectCallerNodeId);
        embeddings.VerifyAll();
    }

    [Theory]
    [InlineData("security")]
    [InlineData("\"Security review\"")]
    public async Task WhenMemoryMatchesWithoutVectors_ThenLexicalSearchFindsItsOwner(string searchText)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await factory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(context, TestContext.Current.CancellationToken);
        await context.CodeNodes.ExecuteUpdateAsync(
            setters => setters.SetProperty(
                node => node.VectorEmbedding,
                (float[]?)null),
            ct);
        context.MemoryNodes.Add(new MemoryNodeRecord
        {
            Id = Guid.NewGuid(),
            TargetCodeNodeId = KnowledgeGraphFixture.DirectCallerNodeId,
            TargetCodeHash = "hash-1",
            Content = "Security review note",
            ContentHash = "memory-hash",
            TagsJson = "[\"security\"]",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync(ct);
        var embeddings = new Mock<IEmbeddingGenerator>();
        embeddings
            .Setup(generator => generator.Generate(searchText, ct))
            .ReturnsAsync(new TextEmbedding(searchText, [1f, 0f]));
        var searcher = new HybridSearcher(
            factory.CreateDbContextFactory(),
            embeddings.Object,
            new SqliteKeywordCandidateProvider());

        var result = await searcher.Search(
            new HybridSearchQuery(
                searchText,
                IncludeMemories: true,
                TagFilters: ["security"]),
            ct);

        result.Value.Hits.Should().ContainSingle().Which.Id.Should().Be(KnowledgeGraphFixture.DirectCallerNodeId);
    }
}
