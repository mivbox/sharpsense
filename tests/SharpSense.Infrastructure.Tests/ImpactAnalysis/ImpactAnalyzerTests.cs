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
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.Seed(context);
        var analyzer = new ImpactAnalyzer(inMemoryFactory.CreateDbContextFactory());

        var result = await analyzer.Analyze(
            new ImpactAnalysisQuery(
                KnowledgeGraphFixture.TargetNodeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                IncludeTransitive: false),
            TestContext.Current.CancellationToken);

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
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.Seed(context);
        var analyzer = new ImpactAnalyzer(inMemoryFactory.CreateDbContextFactory());

        var result = await analyzer.Analyze(
            new ImpactAnalysisQuery(
                KnowledgeGraphFixture.TargetFullyQualifiedName.ToLowerInvariant(),
                MaxDepth: 2,
                IncludeTransitive: true,
                IncludedEdgeTypes: [EdgeType.MethodCall]),
            TestContext.Current.CancellationToken);

        result.TargetSymbol.Should().Be(KnowledgeGraphFixture.TargetFullyQualifiedName);
        result.ImpactedNodes.Should().SatisfyRespectively(
            node => node.Id.Should().Be(KnowledgeGraphFixture.TransitiveCallerNodeId),
            node => node.Id.Should().Be(KnowledgeGraphFixture.DirectCallerNodeId));
        result.Dependencies.Should().AllSatisfy(static edge => edge.EdgeType.Should().Be(EdgeType.MethodCall));
    }
}
