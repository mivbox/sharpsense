using AwesomeAssertions;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.ImpactAnalysis;

public sealed class ImpactAnalyzerTests
{
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
