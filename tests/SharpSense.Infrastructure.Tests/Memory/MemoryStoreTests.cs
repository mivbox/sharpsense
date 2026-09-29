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
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new InMemoryContextFactoryOptions(UseMigrations: true));
        await using (var seedContext = await inMemoryFactory.GetContext(TestContext.Current.CancellationToken))
        {
            await KnowledgeGraphFixture.Seed(seedContext);
            var targetNode = await seedContext.CodeNodes.SingleAsync(
                candidate => candidate.Id == KnowledgeGraphFixture.TargetNodeId,
                TestContext.Current.CancellationToken);
            targetNode.BodyHash = "hash-1";
            await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings.Setup(candidate => candidate.Generate("Needs authentication review", TestContext.Current.CancellationToken))
            .ReturnsAsync(new TextEmbedding("Needs authentication review", [0.1f, 0.2f]));
        var store = new MemoryStore(
            inMemoryFactory.CreateDbContextFactory(),
            embeddings.Object);

        var attachResult = await store.AttachMemory(
            KnowledgeGraphFixture.TargetNodeId,
            " Needs authentication review ",
            [" Security ", "tech-debt", "SECURITY"],
            Domain.KnowledgeGraph.Enums.MemoryIntent.Invariant,
            TestContext.Current.CancellationToken);

        await using (var updateContext = await inMemoryFactory.GetContext(TestContext.Current.CancellationToken))
        {
            var targetNode = await updateContext.CodeNodes.SingleAsync(
                candidate => candidate.Id == KnowledgeGraphFixture.TargetNodeId,
                TestContext.Current.CancellationToken);
            targetNode.BodyHash = "hash-2";
            await updateContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var memoriesByNodeId = await store.GetNodeMemories(
            [KnowledgeGraphFixture.TargetNodeId],
            intents: null,
            TestContext.Current.CancellationToken);

        attachResult.IsSuccess.Should().BeTrue();
        memoriesByNodeId.Should().ContainKey(KnowledgeGraphFixture.TargetNodeId);
        var memory = memoriesByNodeId[KnowledgeGraphFixture.TargetNodeId].Should().ContainSingle().Subject;
        memory.TargetFullyQualifiedName.Should().Be(KnowledgeGraphFixture.TargetFullyQualifiedName);
        memory.Tags.Should().Equal("security", "tech-debt");
        memory.Content.Should().Be("Needs authentication review");
        memory.IsStale.Should().BeTrue();
        embeddings.VerifyAll();
    }

    [Fact]
    public async Task WhenContentHashAlreadyExists_ThenAttachReusesPersistedEmbedding()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new InMemoryContextFactoryOptions(UseMigrations: true));
        await using (var seedContext = await inMemoryFactory.GetContext(TestContext.Current.CancellationToken))
        {
            await KnowledgeGraphFixture.Seed(seedContext);
            foreach (var node in await seedContext.CodeNodes.ToArrayAsync(TestContext.Current.CancellationToken))
            {
                node.BodyHash = "stable-hash";
            }

            await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddings.Setup(candidate => candidate.Generate("Reusable memory", TestContext.Current.CancellationToken))
            .ReturnsAsync(new TextEmbedding("Reusable memory", [0.4f, 0.6f]));
        var store = new MemoryStore(
            inMemoryFactory.CreateDbContextFactory(),
            embeddings.Object);

        var firstResult = await store.AttachMemory(
            KnowledgeGraphFixture.TargetNodeId,
            "Reusable memory",
            ["security"],
            Domain.KnowledgeGraph.Enums.MemoryIntent.Invariant,
            TestContext.Current.CancellationToken);
        var secondResult = await store.AttachMemory(
            KnowledgeGraphFixture.DirectCallerNodeId,
            "Reusable memory",
            ["security"],
            Domain.KnowledgeGraph.Enums.MemoryIntent.Invariant,
            TestContext.Current.CancellationToken);

        await using var verifyContext = await inMemoryFactory.GetContext(TestContext.Current.CancellationToken);
        var persistedEmbeddings = (await verifyContext.MemoryNodes
                .Select(static candidate => new
                {
                    candidate.CreatedAt,
                    candidate.VectorEmbedding
                })
            .ToArrayAsync(TestContext.Current.CancellationToken))
            .OrderBy(static candidate => candidate.CreatedAt)
            .Select(static candidate => candidate.VectorEmbedding)
            .ToArray();

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        persistedEmbeddings.Should().HaveCount(2);
        persistedEmbeddings[0].Should().Equal(persistedEmbeddings[1]);
        embeddings.Verify(candidate => candidate.Generate("Reusable memory", TestContext.Current.CancellationToken), Times.Once);
    }
}
