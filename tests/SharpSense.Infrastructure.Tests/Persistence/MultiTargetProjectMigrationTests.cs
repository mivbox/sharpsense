using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Persistence;

public sealed class MultiTargetProjectMigrationTests
{
    [Fact]
    public async Task WhenUpgradingExistingGraph_ThenPreservesMemoriesAndAllowsSharedProjectDocuments()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(LoadVectorExtension: true));
        var options = new DbContextOptionsBuilder<SharpSenseDbContext>()
            .UseSqlite(database.GetSqliteConnection())
            .Options;
        await using var context = new SharpSenseDbContext(options);
        await context.GetService<IMigrator>()
            .MigrateAsync("20260924000200_GraphRevision", ct);
        await KnowledgeGraphFixture.Seed(context, ct);
        var existingProject = await context.ProjectNodes
            .OrderBy(project => project.Id)
            .FirstAsync(ct);
        var originalProjectId = existingProject.Id;
        var memory = new MemoryNodeRecord
        {
            Id = Guid.NewGuid(),
            TargetCodeNodeId = KnowledgeGraphFixture.TargetNodeId,
            Content = "Keep this authored note",
            ContentHash = "content-hash",
            TargetCodeHash = "target-hash",
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.MemoryNodes.Add(memory);
        await context.SaveChangesAsync(ct);

        await context.Database.MigrateAsync(ct);

        context.ChangeTracker.Clear();
        var retainedMemory = await context.MemoryNodes.SingleAsync(ct);
        retainedMemory.Should().BeEquivalentTo(memory);
        var retainedProject = await context.ProjectNodes.SingleAsync(project => project.Id == originalProjectId, ct);
        retainedProject.Should().BeEquivalentTo(existingProject);
        context.Database.HasPendingModelChanges().Should().BeFalse();
        context.GraphNodes.Add(new GraphNodeRecord
        {
            Id = 999,
            CanonicalId = "project:Fixture.App/Fixture.App.csproj#another-target",
            Kind = GraphNodeKind.Project
        });
        context.ProjectNodes.Add(new ProjectNodeRecord
        {
            Id = 999,
            Name = "Fixture.App(another-target)",
            ProjectDocumentId = existingProject.ProjectDocumentId,
            ContentHash = existingProject.ContentHash
        });
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();

        (await context.ProjectNodes
            .Where(project => project.ProjectDocumentId == existingProject.ProjectDocumentId)
            .Select(project => project.Id)
            .ToArrayAsync(ct)).Should().BeEquivalentTo([originalProjectId, 999]);
        (await context.MemoryNodes.SingleAsync(ct)).Should().BeEquivalentTo(memory);
    }
}
