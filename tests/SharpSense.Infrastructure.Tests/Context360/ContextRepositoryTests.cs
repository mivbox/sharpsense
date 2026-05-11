using AwesomeAssertions;
using SharpSense.Infrastructure.Context360;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Context360;

public sealed class ContextRepositoryTests
{
    [Fact]
    public async Task WhenGetNodeContextHasRelatedMethods_ThenItSanitizesRelatedNodeNamesInProjection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct);
        await KnowledgeGraphFixture.SeedAsync(context);
        var repository = new ContextRepository(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await repository.GetNodeContext(
            KnowledgeGraphFixture.TargetNodeId,
            10,
            ct);

        result.Should().NotBeNull();
        result!.TargetNode.Name.Should().Be(KnowledgeGraphFixture.TargetDisplayName);
        result.Callers.Select(static node => node.Name).Should().Equal(
            "MessageConsumer.Render",
            "ServiceRegistration.Configure");
        result.Implementers.Should().BeEmpty();
        result.Callees.Select(static node => node.Name).Should().Equal(
            "MessageFormatter.Format",
            "Message");
        result.Inherits.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTargetNodeIsMissing_ThenItReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct);
        await KnowledgeGraphFixture.SeedAsync(context);
        var repository = new ContextRepository(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await repository.GetNodeContext(
            999,
            10,
            ct);

        result.Should().BeNull();
    }
}
