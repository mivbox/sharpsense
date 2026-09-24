using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Persistence;

public sealed class MemoryIdentityMigrationTests
{
    private const string PreviousMigration = "20260923000100_DependencyEdgeMetadata";
    private static readonly Guid MemoryId = Guid.Parse("F11E5391-64A7-4F5A-B85D-6CC48B9F18C1");

    [Fact]
    public async Task WhenUpgradingAndRenamingTarget_ThenPreservesEveryMemoryFieldAndUsesNumericForeignKey()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory();
        await using var db = CreateContext(factory);
        await SeedPreviousSchema(db, ct);

        await db.Database.MigrateAsync(ct);

        db.Database.HasPendingModelChanges().Should().BeFalse();
        var memory = await db.MemoryNodes.SingleAsync(ct);
        memory.Id.Should().Be(MemoryId);
        memory.TargetCodeNodeId.Should().Be(7);
        memory.TargetCodeHash.Should().Be("body");
        memory.Content.Should().Be("Authored invariant");
        memory.ContentHash.Should().Be("content-hash");
        memory.TagsJson.Should().Be("[\"important\"]");
        memory.Intent.Should().Be("Invariant");
        memory.VectorEmbedding.Should().Equal(1f, 2f);
        memory.CreatedAt.Should().Be(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
        (await db.Database.SqlQueryRaw<string>("SELECT \"from\" AS Value FROM pragma_foreign_key_list('MemoryNodes')").SingleAsync(ct))
            .Should().Be("TargetCodeNodeId");
        (await db.Database.SqlQueryRaw<string>("SELECT \"to\" AS Value FROM pragma_foreign_key_list('MemoryNodes')").SingleAsync(ct))
            .Should().Be("Id");

        var target = await db.CodeNodes.SingleAsync(ct);
        target.FullyQualifiedName = "Widget<U>";
        await db.SaveChangesAsync(ct);
        (await db.MemoryNodes.SingleAsync(ct)).Id.Should().Be(MemoryId);
        (await db.DependencyEdges.SingleAsync(ct)).Metadata.Should().Be("/api/test");
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM CodeNodeSearch").SingleAsync(ct)).Should().Be(1);

        // Rollback derives the latest name without changing authored memory payloads.
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);
        (await db.Database.SqlQuery<string>($"SELECT TargetFullyQualifiedName AS Value FROM MemoryNodes").SingleAsync(ct))
            .Should().Be("Widget<U>");
        await db.Database.MigrateAsync(ct);
        db.ChangeTracker.Clear();
        (await db.MemoryNodes.SingleAsync(ct)).Id.Should().Be(MemoryId);
        await db.CodeNodes.ExecuteDeleteAsync(ct);
        (await db.MemoryNodes.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task WhenLegacyMemoryHasUnmappedTarget_ThenUpgradeAbortsWithoutDeletingAuthoredRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory();
        await using var db = CreateContext(factory);
        await SeedPreviousSchema(db, ct);
        // Reproduce an invalid pre-existing database without weakening migration constraints.
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;", ct);
        await db.Database.ExecuteSqlRawAsync("UPDATE MemoryNodes SET TargetFullyQualifiedName = 'Missing.Target';", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", ct);

        var migrate = async () => await db.Database.MigrateAsync(ct);
        var failure = await migrate.Should().ThrowAsync<SqliteException>();
        failure.Which.SqliteErrorCode.Should().Be(19);

        (await db.Database.SqlQuery<string>($"SELECT Content AS Value FROM MemoryNodes").SingleAsync(ct))
            .Should().Be("Authored invariant");
        (await db.Database.SqlQuery<Guid>($"SELECT Id AS Value FROM MemoryNodes").SingleAsync(ct)).Should().Be(MemoryId);
        (await db.Database.SqlQuery<string>($"SELECT TargetFullyQualifiedName AS Value FROM MemoryNodes").SingleAsync(ct))
            .Should().Be("Missing.Target");
        (await db.Database.GetAppliedMigrationsAsync(ct)).Should().NotContain("20260923000200_MemoryStableNodeIdentity");
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sqlite_master WHERE name = '__MemoryNodes_StableIdentity'").SingleAsync(ct))
            .Should().Be(0);
    }

    private static SharpSenseDbContext CreateContext(InMemoryContextFactory factory)
        => new(new DbContextOptionsBuilder<SharpSenseDbContext>().UseSqlite(factory.GetSqliteConnection()).Options);

    private static async Task SeedPreviousSchema(SharpSenseDbContext db, CancellationToken ct)
    {
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Directories (Id, ParentId, Path, Name) VALUES (1, NULL, '', '');
            INSERT INTO Documents (Id, DirectoryId, FileName, Extension, RelativePath, Kind)
                VALUES (1, 1, 'Widget.cs', '.cs', 'Widget.cs', 'Source');
            INSERT INTO GraphNodes (Id, CanonicalId, Kind) VALUES (7, 'code:Widget', 'Code');
            INSERT INTO CodeNodes (Id, ProjectNodeId, DocumentId, FullyQualifiedName, DisplayName, NodeType,
                StartLine, EndLine, Summary, SearchText, BodyHash, VectorEmbedding)
                VALUES (7, NULL, 1, 'Widget<T>', 'Widget<T>', 'Class', 1, 3, 'Widget', 'Widget', 'body', NULL);
            INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, SearchText, RelativeFilePath)
                VALUES (7, 'code:Widget', 'Widget<T>', 'Widget<T>', 'Widget', 'Widget.cs');
            INSERT INTO DependencyEdges (CallerNodeId, CalleeNodeId, EdgeType, Metadata)
                VALUES (7, 7, 'HttpRequest', '/api/test');
            INSERT INTO MemoryNodes (Id, TargetFullyQualifiedName, TargetCodeHash, Content, ContentHash,
                TagsJson, Intent, VectorEmbedding, CreatedAt)
                VALUES ('F11E5391-64A7-4F5A-B85D-6CC48B9F18C1', 'Widget<T>', 'body', 'Authored invariant',
                    'content-hash', '["important"]', 'Invariant', X'0000803F00000040', '2026-09-23 00:00:00+00:00');
            """, ct);
    }
}
