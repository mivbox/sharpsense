using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Persistence;

public sealed class TypeScriptMigrationTests
{
    [Fact]
    public async Task WhenUpgradingPopulatedMemorySchema_ThenPreservesAuthoredMemoryAndAddsNullableEdgeMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(LoadVectorExtension: true));
        await using var db = new SharpSenseDbContext(new DbContextOptionsBuilder<SharpSenseDbContext>()
            .UseSqlite(factory.GetSqliteConnection()).Options);
        await db.GetService<IMigrator>()
            .MigrateAsync("20260606032026_MemoryIntentColumn", ct);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Directories (Id, ParentId, Path, Name) VALUES (1, NULL, '', '');
            INSERT INTO Documents (Id, DirectoryId, FileName, Extension, RelativePath, Kind)
                VALUES (1, 1, 'Widget.cs', '.cs', 'Widget.cs', 'Source');
            INSERT INTO GraphNodes (Id, CanonicalId, Kind) VALUES (1, 'code:Widget', 'Code');
            INSERT INTO CodeNodes (Id, ProjectNodeId, DocumentId, FullyQualifiedName, DisplayName, NodeType,
                StartLine, EndLine, Summary, SearchText, BodyHash, VectorEmbedding)
                VALUES (1, NULL, 1, 'Widget<T>', 'Widget<T>', 'Class', 1, 3, 'Widget', 'Widget', 'body', NULL);
            INSERT INTO DependencyEdges (CallerNodeId, CalleeNodeId, EdgeType) VALUES (1, 1, 'MethodCall');
            INSERT INTO MemoryNodes (Id, TargetFullyQualifiedName, TargetCodeHash, Content, ContentHash,
                TagsJson, Intent, VectorEmbedding, CreatedAt)
                VALUES ('F11E5391-64A7-4F5A-B85D-6CC48B9F18C1', 'Widget<T>', 'body', 'Authored invariant',
                'content-hash', '["important"]', 'Invariant', X'0000803F00000040', '2026-09-23 00:00:00+00:00');
            """,
            ct);

        await db.Database.MigrateAsync(ct);

        db.Database.HasPendingModelChanges().Should().BeFalse();
        var memory = await db.MemoryNodes.SingleAsync(ct);
        memory.Id.Should().Be(Guid.Parse("F11E5391-64A7-4F5A-B85D-6CC48B9F18C1"));
        memory.Content.Should().Be("Authored invariant");
        memory.ContentHash.Should().Be("content-hash");
        memory.TargetCodeHash.Should().Be("body");
        memory.TargetCodeNodeId.Should().Be(1);
        memory.TagsJson.Should().Be("[\"important\"]");
        memory.Intent.Should().Be("Invariant");
        memory.VectorEmbedding.Should().Equal(1f, 2f);
        memory.CreatedAt.Should().Be(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));
        (await db.CodeNodes.SingleAsync(ct)).FullyQualifiedName.Should().Be("Widget<T>");
        (await db.DependencyEdges.SingleAsync(ct)).Metadata.Should().BeNull();
    }
}
