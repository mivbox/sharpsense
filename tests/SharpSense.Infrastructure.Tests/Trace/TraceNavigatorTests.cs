using AwesomeAssertions;
using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Infrastructure.Trace;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Trace;

public sealed class TraceNavigatorTests
{
    [Fact]
    public async Task WhenCalleesExceedSqliteParameterLimit_ThenReturnsEveryCalleeInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await factory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(context, ct);
        var callees = await KnowledgeGraphFixture.AddCodeNodes(
            context,
            KnowledgeGraphFixture.TargetNodeId,
            96,
            NodeType.Method,
            ct);
        context.DependencyEdges.AddRange(callees.Select(node => new DependencyEdgeRecord
        {
            CallerNodeId = KnowledgeGraphFixture.TargetNodeId,
            CalleeNodeId = node.Id,
            EdgeType = EdgeType.MethodCall
        }));

        await context.SaveChangesAsync(ct);
        SQLitePCL.raw.sqlite3_limit(
            factory.GetSqliteConnection().Handle!,
            SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER,
            64);
        var navigator = new TraceNavigator(factory.CreateDbContextFactory());

        var result = await navigator.GetCallees(new TraceQuery(KnowledgeGraphFixture.TargetFullyQualifiedName), ct);

        result
            .Select(node => node.Id)
            .Should()
            .Equal(new[]
            {
                KnowledgeGraphFixture.FormatterNodeId,
                KnowledgeGraphFixture.MessageNodeId
            }.Concat(callees.Select(node => node.Id)));
    }

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
