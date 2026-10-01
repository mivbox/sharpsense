using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.HybridSearch;

public sealed class MemoryIdentitySearchTests
{
    [Theory]
    [InlineData("AuthoredMarker", false)]
    [InlineData("RenamedOwner", false)]
    [InlineData("SemanticOnly", true)]
    public async Task WhenMemoryOwnerNameChanges_ThenSearchUsesStableIdAndCurrentName(string searchText, bool vector)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(UseMigrations: true, LoadVectorExtension: true));
        await using var db = await factory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(db, TestContext.Current.CancellationToken);
        foreach (var node in await db.CodeNodes.ToArrayAsync(ct))
        {
            node.VectorEmbedding = null;
        }
        db.MemoryNodes.Add(new MemoryNodeRecord
        {
            Id = Guid.NewGuid(),
            TargetCodeNodeId = KnowledgeGraphFixture.DirectCallerNodeId,
            TargetCodeHash = "hash",
            Content = "AuthoredMarker",
            ContentHash = "memory-hash",
            TagsJson = "[\"identity\"]",
            CreatedAt = DateTimeOffset.UtcNow,
            VectorEmbedding = vector ? [1f, 0f] : null
        });
        await db.SaveChangesAsync(ct);
        var owner = await db.CodeNodes.SingleAsync(node => node.Id == KnowledgeGraphFixture.DirectCallerNodeId, ct);
        owner.FullyQualifiedName = "Fixture.RenamedOwner.Execute()";
        // Leave FTS untouched: only memory-name matching should find this new display name.
        await db.SaveChangesAsync(ct);
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
                TagFilters: ["identity"]),
            ct);

        var hit = result.Value.Hits.Should().ContainSingle().Subject;
        hit.Id.Should().Be(KnowledgeGraphFixture.DirectCallerNodeId);
        hit.FullyQualifiedName.Should().Be(owner.FullyQualifiedName);
        (await searcher.Search(
            new HybridSearchQuery(searchText, IncludeMemories: false),
            ct)).Value.Hits.Should().BeEmpty();
    }
}
