using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharpSense.Application.Memory.ListMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Memory;

public sealed class MemoryStoreTests
{
    [Fact]
    public async Task WhenMemoryOwnersExceedSqliteParameterLimit_ThenReturnsNotesAndEmptyOwners()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await factory.GetContext(ct);
        var memories = await SeedMemories(context, ct);
        var ownerIds = memories
            .Select(memory => memory.TargetCodeNodeId)
            .Append(KnowledgeGraphFixture.TargetNodeId)
            .ToArray();
        SQLitePCL.raw.sqlite3_limit(
            factory.GetSqliteConnection().Handle!,
            SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER,
            64);
        var store = new MemoryStore(
            factory.CreateDbContextFactory(),
            new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object);

        var result = await store.GetNodeMemories([.. ownerIds, int.MaxValue], [MemoryIntent.Decision], ct);

        result.Keys.Should().BeEquivalentTo(ownerIds);
        result[KnowledgeGraphFixture.TargetNodeId].Should().BeEmpty();
        foreach (var expected in memories)
        {
            var actual = result[expected.TargetCodeNodeId].Should().ContainSingle().Subject;
            actual.Id.Should().Be(expected.Id);
            actual.Content.Should().Be(expected.Content);
            actual.TargetFullyQualifiedName.Should().Be($"SharpSense.Graph.Node{expected.TargetCodeNodeId:D4}");
        }
    }

    [Fact]
    public async Task WhenMemoryIdsExceedSqliteParameterLimit_ThenReturnsNotesAndPreservesMissingIds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await factory.GetContext(ct);
        var memories = await SeedMemories(context, ct);
        var missingId = Guid.NewGuid();
        var memoryIds = memories
            .Select(memory => memory.Id)
            .Append(missingId)
            .ToArray();
        SQLitePCL.raw.sqlite3_limit(
            factory.GetSqliteConnection().Handle!,
            SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER,
            64);
        var store = new MemoryStore(
            factory.CreateDbContextFactory(),
            new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object);

        var result = await store.GetMemories([.. memoryIds, memoryIds[0]], ct);

        result.Keys.Should().BeEquivalentTo(memoryIds);
        result[missingId].Should().BeNull();
        foreach (var expected in memories)
        {
            var actual = result[expected.Id];
            actual.Should().NotBeNull();
            actual!.Content.Should().Be(expected.Content);
            actual.TargetFullyQualifiedName.Should().Be($"SharpSense.Graph.Node{expected.TargetCodeNodeId:D4}");
        }
    }

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

    [Fact]
    public async Task WhenListingMemoriesWithFiltersAndPaging_ThenReturnsOrderedSummariesAndAccurateCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(UseMigrations: true));
        var oldestId = Guid.NewGuid();
        var middleId = Guid.NewGuid();
        var newestId = Guid.NewGuid();
        var excludedId = Guid.NewGuid();
        await using (var seedContext = await inMemoryFactory.GetContext(ct))
        {
            await KnowledgeGraphFixture.Seed(seedContext, ct);
            var targetNode = await seedContext.CodeNodes
                .SingleAsync(candidate => candidate.Id == KnowledgeGraphFixture.TargetNodeId, ct);
            targetNode.BodyHash = "current-hash";
            var callerNode = await seedContext.CodeNodes
                .SingleAsync(candidate => candidate.Id == KnowledgeGraphFixture.DirectCallerNodeId, ct);
            callerNode.BodyHash = "stable-hash";
            seedContext.MemoryNodes.AddRange(
                new MemoryNodeRecord
                {
                    Id = oldestId,
                    TargetCodeNodeId = KnowledgeGraphFixture.TargetNodeId,
                    TargetCodeHash = "old-hash",
                    Content = "Keep   SQLite\ntransactions   bounded when traversing.",
                    ContentHash = "h1",
                    TagsJson = "[\"sqlite\",\"perf\"]",
                    Intent = nameof(MemoryIntent.Invariant),
                    CreatedAt = DateTimeOffset.Parse("2026-05-01T00:00:00+00:00")
                },
                new MemoryNodeRecord
                {
                    Id = middleId,
                    TargetCodeNodeId = KnowledgeGraphFixture.DirectCallerNodeId,
                    TargetCodeHash = "stable-hash",
                    Content = new string('x', 200) + " sqlite",
                    ContentHash = "h2",
                    TagsJson = "[\"sqlite\",\"perf\"]",
                    Intent = nameof(MemoryIntent.Invariant),
                    CreatedAt = DateTimeOffset.Parse("2026-05-02T00:00:00+00:00")
                },
                new MemoryNodeRecord
                {
                    Id = newestId,
                    TargetCodeNodeId = KnowledgeGraphFixture.TargetNodeId,
                    TargetCodeHash = "old-hash-2",
                    Content = "Another SQLite note.",
                    ContentHash = "h3",
                    TagsJson = "[\"sqlite\",\"perf\"]",
                    Intent = nameof(MemoryIntent.Warning),
                    CreatedAt = DateTimeOffset.Parse("2026-05-03T00:00:00+00:00")
                },
                new MemoryNodeRecord
                {
                    Id = excludedId,
                    TargetCodeNodeId = KnowledgeGraphFixture.DirectCallerNodeId,
                    TargetCodeHash = "stable-hash",
                    Content = "Unrelated decision",
                    ContentHash = "h4",
                    TagsJson = "[\"ui\"]",
                    Intent = nameof(MemoryIntent.Decision),
                    CreatedAt = DateTimeOffset.Parse("2026-05-04T00:00:00+00:00")
                });

            await seedContext.SaveChangesAsync(ct);
        }

        var store = new MemoryStore(
            inMemoryFactory.CreateDbContextFactory(),
            new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object);

        var firstPage = await store.ListMemories(
            new ListMemoriesQuery(
                Intents: [MemoryIntent.Invariant, MemoryIntent.Warning],
                Tags: ["sqlite", "perf"],
                StaleOnly: false,
                SearchText: "SQLITE",
                Limit: 2,
                Offset: 0),
            ct);
        var staleOnly = await store.ListMemories(
            new ListMemoriesQuery(StaleOnly: true, Limit: 10, Offset: 0),
            ct);

        firstPage.TotalCount.Should().Be(3);
        firstPage.StaleCount.Should().Be(2);
        firstPage.HasMore.Should().BeTrue();
        firstPage.Items.Select(static item => item.Id).Should().Equal(newestId, middleId);
        firstPage.Items[0].TargetCodeNodeId.Should().Be(KnowledgeGraphFixture.TargetNodeId);
        firstPage.Items[0].TargetRelativeFilePath.Should().NotBeNullOrWhiteSpace();
        firstPage.Items[0].IsStale.Should().BeTrue();
        firstPage.Items[1].IsStale.Should().BeFalse();
        firstPage.Items[1].ContentPreview.Should().HaveLength(160);
        firstPage.Items[1].ContentPreview.Should().EndWith("...");

        staleOnly.TotalCount.Should().Be(2);
        staleOnly.StaleCount.Should().Be(2);
        staleOnly.HasMore.Should().BeFalse();
        staleOnly.Items.Select(static item => item.Id).Should().Equal(newestId, oldestId);
        staleOnly.Items[1].ContentPreview.Should().Be("Keep SQLite transactions bounded when traversing.");
    }

    private static async Task<MemoryNodeRecord[]> SeedMemories(SharpSenseDbContext context, CancellationToken ct)
    {
        await KnowledgeGraphFixture.Seed(context, ct);
        var nodes = await KnowledgeGraphFixture.AddCodeNodes(
            context,
            KnowledgeGraphFixture.TargetNodeId,
            96,
            NodeType.Method,
            ct);
        var memories = nodes
            .Select(node => new MemoryNodeRecord
            {
                Id = Guid.NewGuid(),
                TargetCodeNodeId = node.Id,
                Content = $"Preserve graph node {node.Id} during reindexing.",
                Intent = nameof(MemoryIntent.Decision)
            })
            .ToArray();
        context.MemoryNodes.AddRange(memories);

        await context.SaveChangesAsync(ct);

        return memories;
    }
}
