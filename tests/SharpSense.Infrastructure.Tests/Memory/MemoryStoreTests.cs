using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Memory;

public sealed class MemoryStoreTests
{
    [Fact]
    public async Task WhenBodyHashChangesAfterAttach_ThenReturnedMemoryIsMarkedStale()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(UseMigrations: true));
        await using (var seedContext = await inMemoryFactory.GetContext(ct))
        {
            await KnowledgeGraphFixture.Seed(seedContext, ct);
            var targetNode = await seedContext.CodeNodes
                .SingleAsync(
                    candidate => candidate.Id == KnowledgeGraphFixture.TargetNodeId,
                    ct);
            targetNode.BodyHash = "hash-1";
            await seedContext.SaveChangesAsync(ct);
        }

        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings
            .Setup(candidate => candidate.Generate("Needs authentication review", ct))
            .ReturnsAsync(new TextEmbedding("Needs authentication review", [0.1f, 0.2f]));
        var store = new MemoryStore(
            inMemoryFactory.CreateDbContextFactory(),
            embeddings.Object);

        var attachResult = await store.AttachMemory(
            KnowledgeGraphFixture.TargetNodeId,
            " Needs authentication review ",
            [" Security ", "tech-debt", "SECURITY"],
            Domain.KnowledgeGraph.Enums.MemoryIntent.Invariant,
            ct);

        attachResult.IsSuccess.Should().BeTrue();
        attachResult.Value.IsStale.Should().BeFalse();

        await using (var updateContext = await inMemoryFactory.GetContext(ct))
        {
            var targetNode = await updateContext.CodeNodes
                .SingleAsync(
                    candidate => candidate.Id == KnowledgeGraphFixture.TargetNodeId,
                    ct);
            targetNode.BodyHash = "hash-2";
            await updateContext.SaveChangesAsync(ct);
        }

        var memoriesByNodeId = await store.GetNodeMemories(
            [KnowledgeGraphFixture.TargetNodeId],
            intents: null,
            ct);

        memoriesByNodeId.Should().ContainKey(KnowledgeGraphFixture.TargetNodeId);
        var memory = memoriesByNodeId[KnowledgeGraphFixture.TargetNodeId].Should().ContainSingle().Subject;
        memory.TargetFullyQualifiedName.Should().Be(KnowledgeGraphFixture.TargetFullyQualifiedName);
        memory.Tags.Should().Equal("security", "tech-debt");
        memory.Content.Should().Be("Needs authentication review");
        memory.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task WhenContentHashAlreadyExists_ThenAttachReusesPersistedEmbedding()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(UseMigrations: true));
        await using (var seedContext = await inMemoryFactory.GetContext(ct))
        {
            await KnowledgeGraphFixture.Seed(seedContext, ct);
            foreach (var node in await seedContext.CodeNodes.ToArrayAsync(ct))
            {
                node.BodyHash = "stable-hash";
            }

            await seedContext.SaveChangesAsync(ct);
        }

        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings
            .Setup(candidate => candidate.Generate("Reusable memory", ct))
            .ReturnsAsync(new TextEmbedding("Reusable memory", [0.4f, 0.6f]));
        var store = new MemoryStore(
            inMemoryFactory.CreateDbContextFactory(),
            embeddings.Object);

        var firstResult = await store.AttachMemory(
            KnowledgeGraphFixture.TargetNodeId,
            "Reusable memory",
            ["security"],
            Domain.KnowledgeGraph.Enums.MemoryIntent.Invariant,
            ct);
        var secondResult = await store.AttachMemory(
            KnowledgeGraphFixture.DirectCallerNodeId,
            "Reusable memory",
            ["security"],
            Domain.KnowledgeGraph.Enums.MemoryIntent.Invariant,
            ct);

        await using var verifyContext = await inMemoryFactory.GetContext(ct);
        var persistedEmbeddings = await verifyContext.MemoryNodes
            .Select(candidate => candidate.VectorEmbedding)
            .ToArrayAsync(ct);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        persistedEmbeddings.Should().HaveCount(2);
        persistedEmbeddings.Should().AllSatisfy(vector => vector.Should().Equal(0.4f, 0.6f));
        embeddings.Verify(
            candidate => candidate.Generate("Reusable memory", ct),
            Times.Once);
    }
}
