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
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
        LimitSqlParameters(factory);
        var collectionSql = new List<string>();
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>(
            message =>
            {
                if (message.Contains("json_each", StringComparison.Ordinal))
                {
                    collectionSql.Add(message);
                }
            }));

        await repository.ReplaceTarget(Snapshot(0, 128), ct);
        var survivorId = await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node64")
            .Select(node => node.Id).SingleAsync(ct);
        var memory = Memory(survivorId);
        context.MemoryNodes.Add(memory);
        await context.SaveChangesAsync(ct);

        await repository.ReplaceTarget(Snapshot(64, 128), ct);
        context.ChangeTracker.Clear();

        Assert.Equal(128, await context.CodeNodes.CountAsync(ct));
        Assert.Equal(128, await context.GraphNodes.CountAsync(ct));
        Assert.Equal(128, await context.Documents.CountAsync(ct));
        Assert.Equal(130, await context.Directories.CountAsync(ct));
        Assert.Equal(127, await context.DependencyEdges.CountAsync(ct));
        Assert.Equal(survivorId, await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node64")
            .Select(node => node.Id).SingleAsync(ct));
        Assert.Equal(memory.Id, await context.MemoryNodes.Select(node => node.Id).SingleAsync(ct));
        Assert.False(await context.CodeNodes.AnyAsync(node => node.FullyQualifiedName == "Fixture.Node0", ct));
        Assert.Equal(128, await SearchRowCount(context, ct));
        Assert.Contains(collectionSql, sql => sql.Contains("NOT IN", StringComparison.Ordinal));

        await repository.ReplaceTarget(new([], [], [], []), ct);

        Assert.Equal(0, await context.GraphNodes.CountAsync(ct));
        Assert.Equal(0, await context.MemoryNodes.CountAsync(ct));
        Assert.Equal(0, await context.Documents.CountAsync(ct));
        Assert.Equal(0, await SearchRowCount(context, ct));
    }

    [Fact]
    public async Task WhenUpdatingManyFiles_ThenBoundsReadsAndDeletesAndRetainsUnchangedNodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>());
        var original = Snapshot(0, 160);
        await repository.ReplaceTarget(original, ct);
        var survivorId = await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node64")
            .Select(node => node.Id).SingleAsync(ct);
        context.MemoryNodes.Add(Memory(survivorId));
        await context.SaveChangesAsync(ct);
        LimitSqlParameters(factory);

        var changedPaths = original.CodeNodes.Take(128).Select(node => node.RelativeFilePath).ToArray();
        Assert.Equal(128, (await repository.GetPersistedCodeNodes(changedPaths, ct)).Count);
        var update = Snapshot(64, 64);
        update = update with
        {
            CodeNodes = update.CodeNodes.Select(node => node with { BodyHash = "updated", Summary = "updated" }).ToArray(),
            Edges = [.. update.Edges, new(update.CodeNodes[^1].CanonicalId, original.CodeNodes[128].CanonicalId, EdgeType.MethodCall)]
        };

        await repository.ReplaceWorkspaceFiles(changedPaths, update, ct);
        context.ChangeTracker.Clear();

        Assert.Equal(96, await context.CodeNodes.CountAsync(ct));
        Assert.Equal(96, await context.Documents.CountAsync(ct));
        Assert.Equal(95, await context.DependencyEdges.CountAsync(ct));
        Assert.Equal(96, await SearchRowCount(context, ct));
        Assert.Equal(survivorId, await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node64")
            .Select(node => node.Id).SingleAsync(ct));
        Assert.Equal(1, await context.MemoryNodes.CountAsync(ct));
        Assert.Equal("updated", await context.CodeNodes.Where(node => node.Id == survivorId)
            .Select(node => node.BodyHash).SingleAsync(ct));
        Assert.Equal("original", await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node159")
            .Select(node => node.BodyHash).SingleAsync(ct));
    }

    [Fact]
    public async Task WhenLargeReplacementFailsAfterWritingNodes_ThenRollsBackGraphAndMemories()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>());
        var original = Snapshot(0, 128);
        await repository.ReplaceTarget(original, ct);
        var originalIds = await context.GraphNodes.OrderBy(node => node.Id).Select(node => node.Id).ToArrayAsync(ct);
        var originalRevision = await context.IndexRunState.Select(state => state.GraphRevision).SingleAsync(ct);
        var removedId = await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Node0")
            .Select(node => node.Id).SingleAsync(ct);
        var memory = Memory(removedId);
        context.MemoryNodes.Add(memory);
        await context.SaveChangesAsync(ct);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER RejectObsoleteDocumentPrune
            BEFORE DELETE ON Documents WHEN OLD.RelativePath = 'src/D0/Node.cs'
            BEGIN SELECT RAISE(ABORT, 'injected prune failure'); END;
            """, ct);
        LimitSqlParameters(factory);

        var failure = await Assert.ThrowsAsync<SqliteException>(
            () => repository.ReplaceTarget(Snapshot(64, 128), ct));

        Assert.Contains("injected prune failure", failure.Message);
        context.ChangeTracker.Clear();
        Assert.Equal(originalIds, await context.GraphNodes.OrderBy(node => node.Id).Select(node => node.Id).ToArrayAsync(ct));
        Assert.Equal(128, await context.Documents.CountAsync(ct));
        Assert.Equal(127, await context.DependencyEdges.CountAsync(ct));
        Assert.Equal(memory.Id, await context.MemoryNodes.Select(node => node.Id).SingleAsync(ct));
        Assert.Equal(128, await SearchRowCount(context, ct));
        Assert.False(await context.CodeNodes.AnyAsync(node => node.FullyQualifiedName == "Fixture.Node191", ct));
        Assert.Equal(originalRevision, await context.IndexRunState.Select(state => state.GraphRevision).SingleAsync(ct));
    }

    private static void LimitSqlParameters(InMemoryContextFactory factory)
    {
        // Exercise SQLite's real limit with a small fixture, rather than depending on
        // the native library's build-specific maximum (usually 32,766 parameters).
        SQLitePCL.raw.sqlite3_limit(factory.GetSqliteConnection().Handle!, SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER, 64);
    }

    private static ExtractedNodes Snapshot(int start, int count)
    {
        var nodes = Enumerable.Range(start, count).Select(index => new IndexedCodeNode(
            $"code:{index}", null, $"Fixture.Node{index}", $"Node{index}", NodeType.Class,
            $"src/D{index}/Node.cs", 1, 3, "original", $"Node{index}", "original")).ToArray();
        return new([], nodes, nodes.Zip(nodes.Skip(1),
            (caller, callee) => new IndexedDependency(caller.CanonicalId, callee.CanonicalId, EdgeType.MethodCall)).ToArray(), []);
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
        => context.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM CodeNodeSearch").SingleAsync(ct);
}
