using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Infrastructure.Trace;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Trace;

public sealed class TraceNavigatorTests
{
    [Fact]
    public async Task WhenGetCalleesUsesCaseInsensitiveIdentifier_ThenReturnsDistinctOrderedCallees()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.SeedAsync(context);
        var navigator = new TraceNavigator(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await navigator.GetCallees(
            new TraceQuery(KnowledgeGraphFixture.TargetFullyQualifiedName.ToLowerInvariant()),
            TestContext.Current.CancellationToken);

        Assert.Collection(
            result,
            node => Assert.Equal(KnowledgeGraphFixture.FormatterNodeId, node.Id),
            node => Assert.Equal(KnowledgeGraphFixture.MessageNodeId, node.Id));
    }

    [Fact]
    public async Task WhenGetCalleesIncludesStructuralEdges_ThenItReturnsChildNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.SeedAsync(context);
        var navigator = new TraceNavigator(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await navigator.GetCallees(
            new TraceQuery(
                KnowledgeGraphFixture.MessageProviderTypeFullyQualifiedName,
                KnowledgeGraphEdgeTypes.All),
            TestContext.Current.CancellationToken);

        Assert.Collection(
            result,
            node => Assert.Equal(KnowledgeGraphFixture.CachedMessageNodeId, node.Id),
            node => Assert.Equal(KnowledgeGraphFixture.TargetNodeId, node.Id));
    }
}
