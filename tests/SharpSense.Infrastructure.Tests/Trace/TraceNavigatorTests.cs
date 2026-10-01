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
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(context, ct);
        var navigator = new TraceNavigator(inMemoryFactory.CreateDbContextFactory());

        var result = await navigator.GetCallees(
            new TraceQuery(KnowledgeGraphFixture.TargetFullyQualifiedName.ToLowerInvariant()),
            ct);

        result.Should().SatisfyRespectively(
            node => node.Id.Should().Be(KnowledgeGraphFixture.FormatterNodeId),
            node => node.Id.Should().Be(KnowledgeGraphFixture.MessageNodeId));
    }

    [Fact]
    public async Task WhenGetCalleesIncludesStructuralEdges_ThenItReturnsChildNodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(context, ct);
        var navigator = new TraceNavigator(inMemoryFactory.CreateDbContextFactory());

        var result = await navigator.GetCallees(
            new TraceQuery(KnowledgeGraphFixture.MessageProviderTypeFullyQualifiedName, KnowledgeGraphEdgeTypes.All),
            ct);

        result.Should().SatisfyRespectively(
            node => node.Id.Should().Be(KnowledgeGraphFixture.CachedMessageNodeId),
            node => node.Id.Should().Be(KnowledgeGraphFixture.TargetNodeId));
    }
}
