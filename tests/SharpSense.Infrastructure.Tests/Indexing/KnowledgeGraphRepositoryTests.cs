using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class KnowledgeGraphRepositoryTests
{
    [Fact]
    public async Task WhenReplacingTargetWithMissingProjectReference_ThenPersistsNullProjectNodeId()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        await repository.ReplaceTarget(
            new ExtractedNodes(
                [],
                [
                    new IndexedCodeNode(
                        "code:fixture:src/Fixture/Orphan.cs:T:Orphan",
                        "project:missing",
                        "Fixture.Orphan",
                        "Orphan",
                        NodeType.Class,
                        "src/Fixture/Orphan.cs",
                        1,
                        12,
                        "Orphan node.")
                ],
                [],
                []),
            TestContext.Current.CancellationToken);

        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var persistedNode = await context.CodeNodes
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        persistedNode.ProjectNodeId.Should().BeNull();
        persistedNode.FullyQualifiedName.Should().Be("Fixture.Orphan");
    }
}
