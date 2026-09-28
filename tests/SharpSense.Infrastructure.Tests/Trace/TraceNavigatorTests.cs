using AwesomeAssertions;
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
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.Seed(context);
        var navigator = new TraceNavigator(inMemoryFactory.CreateDbContextFactory());

        var result = await navigator.GetCallees(
            new TraceQuery(KnowledgeGraphFixture.TargetFullyQualifiedName.ToLowerInvariant()),
            TestContext.Current.CancellationToken);

        result.Should().SatisfyRespectively(
            node => node.Id.Should().Be(KnowledgeGraphFixture.FormatterNodeId),
            node => node.Id.Should().Be(KnowledgeGraphFixture.MessageNodeId));
    }

    [Fact]
    public async Task WhenGetCalleesIncludesStructuralEdges_ThenItReturnsChildNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.Seed(context);
        var navigator = new TraceNavigator(inMemoryFactory.CreateDbContextFactory());

        var result = await navigator.GetCallees(
            new TraceQuery(
                KnowledgeGraphFixture.MessageProviderTypeFullyQualifiedName,
                KnowledgeGraphEdgeTypes.All),
            TestContext.Current.CancellationToken);

        result.Should().SatisfyRespectively(
            node => node.Id.Should().Be(KnowledgeGraphFixture.CachedMessageNodeId),
            node => node.Id.Should().Be(KnowledgeGraphFixture.TargetNodeId));
    }
}
