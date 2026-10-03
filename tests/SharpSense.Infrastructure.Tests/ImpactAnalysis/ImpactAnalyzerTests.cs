using AwesomeAssertions;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.ImpactAnalysis;

public sealed class ImpactAnalyzerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WhenCallersExceedSqliteParameterLimit_ThenReturnsEveryCallerWithinDepth(int maxDepth)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await factory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(context, ct);
        var callers = await KnowledgeGraphFixture.AddCodeNodes(
            context,
            KnowledgeGraphFixture.TargetNodeId,
            96,
            NodeType.Method,
            ct);
        foreach (var caller in callers)
        {
            context.DependencyEdges.AddRange(
                new DependencyEdgeRecord
                {
                    CallerNodeId = caller.Id,
                    CalleeNodeId = KnowledgeGraphFixture.TargetNodeId,
                    EdgeType = EdgeType.MethodCall
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = KnowledgeGraphFixture.TransitiveCallerNodeId,
                    CalleeNodeId = caller.Id,
                    EdgeType = EdgeType.MethodCall
                });
        }

        await context.SaveChangesAsync(ct);
        SQLitePCL.raw.sqlite3_limit(
            factory.GetSqliteConnection().Handle!,
            SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER,
            64);
        var analyzer = new ImpactAnalyzer(factory.CreateDbContextFactory());

        var result = await analyzer.Analyze(
            new ImpactAnalysisQuery(
                KnowledgeGraphFixture.TargetFullyQualifiedName,
                MaxDepth: maxDepth,
                IncludedEdgeTypes: [EdgeType.MethodCall]),
            ct);

        var expectedIds = callers
            .Select(node => node.Id)
            .Append(KnowledgeGraphFixture.DirectCallerNodeId);
        var expectedEdges = callers
            .Select(node => ($"code:sharpsense:{node.Id}", KnowledgeGraphFixture.TargetCanonicalId))
            .Append((KnowledgeGraphFixture.DirectCallerCanonicalId, KnowledgeGraphFixture.TargetCanonicalId));
        if (maxDepth == 2)
        {
            expectedIds = expectedIds.Append(KnowledgeGraphFixture.TransitiveCallerNodeId);
            expectedEdges = expectedEdges
                .Concat(callers.Select(node => (KnowledgeGraphFixture.TransitiveCallerCanonicalId, $"code:sharpsense:{node.Id}")))
                .Append((KnowledgeGraphFixture.TransitiveCallerCanonicalId, KnowledgeGraphFixture.DirectCallerCanonicalId));
        }

        result.ImpactedNodes
            .Select(node => node.Id)
            .Should()
            .BeEquivalentTo(expectedIds);
        result.Dependencies
            .Select(edge => (edge.CallerId, edge.CalleeId))
            .Should()
            .BeEquivalentTo(expectedEdges);
        result.Dependencies.Should().OnlyContain(edge => edge.EdgeType == EdgeType.MethodCall);
    }

    [Fact]
    public async Task WhenAnalyzeWithoutTransitiveTraversal_ThenReturnsDirectInboundDependencies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(context, ct);
        var analyzer = new ImpactAnalyzer(inMemoryFactory.CreateDbContextFactory());

        var result = await analyzer.Analyze(
            new ImpactAnalysisQuery(
                KnowledgeGraphFixture.TargetNodeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                IncludeTransitive: false),
            ct);

        result.TargetSymbol.Should().Be(KnowledgeGraphFixture.TargetFullyQualifiedName);
        result.ImpactedNodes.Should().SatisfyRespectively(
            node => node.Id.Should().Be(KnowledgeGraphFixture.DirectCallerNodeId),
            node => node.Id.Should().Be(KnowledgeGraphFixture.ServiceRegistrationCallerNodeId));
        result.Dependencies.Should().SatisfyRespectively(
            edge =>
            {
                edge.CallerId.Should().Be(KnowledgeGraphFixture.DirectCallerCanonicalId);
                edge.EdgeType.Should().Be(EdgeType.MethodCall);
            },
            edge =>
            {
                edge.CallerId.Should().Be(KnowledgeGraphFixture.ServiceRegistrationCallerCanonicalId);
                edge.EdgeType.Should().Be(EdgeType.ServiceRegistration);
            });
    }

    [Fact]
    public async Task WhenAnalyzeWithMethodCallFilter_ThenReturnsOnlyTransitiveMethodCallers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct);
        await KnowledgeGraphFixture.Seed(context, ct);
        var analyzer = new ImpactAnalyzer(inMemoryFactory.CreateDbContextFactory());

        var result = await analyzer.Analyze(
            new ImpactAnalysisQuery(
                KnowledgeGraphFixture.TargetFullyQualifiedName.ToLowerInvariant(),
                MaxDepth: 2,
                IncludeTransitive: true,
                IncludedEdgeTypes: [EdgeType.MethodCall]),
            ct);

        result.TargetSymbol.Should().Be(KnowledgeGraphFixture.TargetFullyQualifiedName);
        result.ImpactedNodes.Should().SatisfyRespectively(
            node => node.Id.Should().Be(KnowledgeGraphFixture.TransitiveCallerNodeId),
            node => node.Id.Should().Be(KnowledgeGraphFixture.DirectCallerNodeId));
        result.Dependencies
            .Select(edge => (edge.CallerId, edge.CalleeId, edge.EdgeType))
            .Should()
            .BeEquivalentTo(new[]
            {
                (KnowledgeGraphFixture.DirectCallerCanonicalId, KnowledgeGraphFixture.TargetCanonicalId, EdgeType.MethodCall),
                (KnowledgeGraphFixture.TransitiveCallerCanonicalId, KnowledgeGraphFixture.DirectCallerCanonicalId, EdgeType.MethodCall)
            });
    }
}
