using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class KnowledgeGraphRepositoryParameterLimitTests
{
    [Fact]
    public async Task WhenReplacingLargeGraph_ThenBoundsParametersAndPreservesSurvivingIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await factory.GetContext(ct);
        LimitSqlParameters(factory);
        var collectionSql = new List<string>();
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory(
            message =>
            {
                if (message.Contains("json_each", StringComparison.Ordinal))
                {
                    collectionSql.Add(message);
                }
            }));

        await repository.ReplaceWorkspace(Snapshot(0, 128), ct);
        var survivorId = await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node64")
            .Select(node => node.Id)
            .SingleAsync(ct);
        var memory = Memory(survivorId);
        context.MemoryNodes.Add(memory);
        await context.SaveChangesAsync(ct);

        await repository.ReplaceWorkspace(Snapshot(64, 128), ct);
        context.ChangeTracker.Clear();

        (await context.CodeNodes.CountAsync(ct)).Should().Be(128);
        (await context.GraphNodes.CountAsync(ct)).Should().Be(128);
        (await context.Documents.CountAsync(ct)).Should().Be(128);
        (await context.Directories.CountAsync(ct)).Should().Be(130);
        (await context.DependencyEdges.CountAsync(ct)).Should().Be(127);
        (await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node64")
            .Select(node => node.Id)
            .SingleAsync(ct)).Should().Be(survivorId);
        (await context.MemoryNodes.Select(node => node.Id)
            .SingleAsync(ct)).Should().Be(memory.Id);
        (await context.CodeNodes.AnyAsync(node => node.FullyQualifiedName == "Fixture.Node0", ct)).Should().BeFalse();
        (await SearchRowCount(context, ct)).Should().Be(128);
        collectionSql.Should().Contain(sql => sql.Contains("NOT IN", StringComparison.Ordinal));

        await repository.ReplaceWorkspace(new([], [], [], []), ct);

        (await context.GraphNodes.CountAsync(ct)).Should().Be(0);
        (await context.MemoryNodes.CountAsync(ct)).Should().Be(0);
        (await context.Documents.CountAsync(ct)).Should().Be(0);
        (await SearchRowCount(context, ct)).Should().Be(0);
    }

    [Fact]
    public async Task WhenUpdatingManyFiles_ThenBoundsReadsAndDeletesAndRetainsUnchangedNodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await factory.GetContext(ct);
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        var original = Snapshot(0, 160);
        await repository.ReplaceWorkspace(original, ct);
        var survivorId = await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node64")
            .Select(node => node.Id)
            .SingleAsync(ct);
        context.MemoryNodes.Add(Memory(survivorId));
        await context.SaveChangesAsync(ct);
        LimitSqlParameters(factory);

        (await repository.GetPersistedCodeNodes(ct)).Count.Should().Be(160);
        var update = Snapshot(64, 64);
        update = update with
        {
            CodeNodes = update.CodeNodes.Select(node => node with
            {
                BodyHash = "updated",
                Summary = "updated"
            })
                .ToArray(),
            Edges = [.. update.Edges, new(update.CodeNodes[^1].CanonicalId, original.CodeNodes[128].CanonicalId, EdgeType.MethodCall)]
        };

        await repository.ReplaceWorkspace(
            update with
            {
                CodeNodes = [.. update.CodeNodes, .. original.CodeNodes.Skip(128)],
                Edges = [.. update.Edges, .. original.Edges.Skip(128)]
            },
            ct);
        context.ChangeTracker.Clear();

        (await context.CodeNodes.CountAsync(ct)).Should().Be(96);
        (await context.Documents.CountAsync(ct)).Should().Be(96);
        (await context.DependencyEdges.CountAsync(ct)).Should().Be(95);
        (await SearchRowCount(context, ct)).Should().Be(96);
        (await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node64")
            .Select(node => node.Id)
            .SingleAsync(ct)).Should().Be(survivorId);
        (await context.MemoryNodes.CountAsync(ct)).Should().Be(1);
        (await context.CodeNodes.Where(node => node.Id == survivorId)
            .Select(node => node.BodyHash)
            .SingleAsync(ct)).Should().Be("updated");
        (await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node159")
            .Select(node => node.BodyHash)
            .SingleAsync(ct)).Should().Be("original");
    }

    [Fact]
    public async Task WhenLargeReplacementFailsAfterWritingNodes_ThenRollsBackGraphAndMemories()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await factory.GetContext(ct);
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        var original = Snapshot(0, 128);
        await repository.ReplaceWorkspace(original, ct);
        var originalIds = await context.GraphNodes.OrderBy(node => node.Id)
            .Select(node => node.Id)
            .ToArrayAsync(ct);
        var originalRevision = await context.IndexRunState.Select(state => state.GraphRevision)
            .SingleAsync(ct);
        var removedId = await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node0")
            .Select(node => node.Id)
            .SingleAsync(ct);
        var memory = Memory(removedId);
        context.MemoryNodes.Add(memory);
        await context.SaveChangesAsync(ct);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER RejectObsoleteDocumentPrune
            BEFORE DELETE ON Documents WHEN OLD.RelativePath = 'src/D0/Node.cs'
            BEGIN SELECT RAISE(ABORT, 'injected prune failure'); END;
            """,
            ct);
        LimitSqlParameters(factory);

        var failure = (await ((Func<Task>)(() => repository.ReplaceWorkspace(Snapshot(64, 128), ct))).Should().ThrowExactlyAsync<SqliteException>()).Which;

        failure.Message.Should().Contain("injected prune failure");
        context.ChangeTracker.Clear();
        (await context.GraphNodes.OrderBy(node => node.Id)
            .Select(node => node.Id)
            .ToArrayAsync(ct)).Should().Equal(originalIds);
        (await context.Documents.CountAsync(ct)).Should().Be(128);
        (await context.DependencyEdges.CountAsync(ct)).Should().Be(127);
        (await context.MemoryNodes.Select(node => node.Id)
            .SingleAsync(ct)).Should().Be(memory.Id);
        (await SearchRowCount(context, ct)).Should().Be(128);
        (await context.CodeNodes.AnyAsync(node => node.FullyQualifiedName == "Fixture.Node191", ct)).Should().BeFalse();
        (await context.IndexRunState.Select(state => state.GraphRevision)
            .SingleAsync(ct)).Should().Be(originalRevision);
    }

    private static void LimitSqlParameters(InMemoryContextFactory<SharpSenseDbContext> factory)
    {
        // Exercise SQLite's real limit with a small fixture, rather than depending on
        // the native library's build-specific maximum (usually 32,766 parameters).
        SQLitePCL.raw.sqlite3_limit(
            factory.GetSqliteConnection().Handle!,
            SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER,
            64);
    }

    private static ExtractedNodes Snapshot(int start, int count)
    {
        var nodes = Enumerable.Range(start, count)
            .Select(index => new IndexedCodeNode(
                $"code:{index}",
                null,
                $"Fixture.Node{index}",
                $"Node{index}",
                NodeType.Class,
                $"src/D{index}/Node.cs",
                1,
                3,
                "original",
                $"Node{index}",
                "original"))
            .ToArray();

        return new(
            [],
            nodes,
            nodes.Zip(
                nodes.Skip(1),
                (caller, callee) => new IndexedDependency(caller.CanonicalId, callee.CanonicalId, EdgeType.MethodCall))
                .ToArray(),
            []);
    }

    private static MemoryNodeRecord Memory(int nodeId)
        => new()
        {
            Id = Guid.NewGuid(),
            TargetCodeNodeId = nodeId,
            TargetCodeHash = "original",
            Content = "Retained context",
            ContentHash = "memory",
            TagsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static Task<int> SearchRowCount(SharpSenseDbContext context, CancellationToken ct)
        => context.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM CodeNodeSearch")
            .SingleAsync(ct);
}
