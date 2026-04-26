using SharpSense.Application.Features.ImpactAnalysis.ImpactAnalysis;
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
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.SeedAsync(context);
        var analyzer = new ImpactAnalyzer(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await analyzer.Analyze(
            new ImpactAnalysisQuery(KnowledgeGraphFixture.TargetNodeId.ToString(System.Globalization.CultureInfo.InvariantCulture), IncludeTransitive: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(KnowledgeGraphFixture.TargetFullyQualifiedName, result.TargetSymbol);
        Assert.Collection(
            result.ImpactedNodes,
            node => Assert.Equal(KnowledgeGraphFixture.DirectCallerNodeId, node.Id),
            node => Assert.Equal(KnowledgeGraphFixture.ServiceRegistrationCallerNodeId, node.Id));
        Assert.Collection(
            result.Dependencies,
            edge =>
            {
                Assert.Equal(KnowledgeGraphFixture.DirectCallerCanonicalId, edge.CallerId);
                Assert.Equal(EdgeType.MethodCall, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal(KnowledgeGraphFixture.ServiceRegistrationCallerCanonicalId, edge.CallerId);
                Assert.Equal(EdgeType.ServiceRegistration, edge.EdgeType);
            });
    }

    [Fact]
    public async Task WhenAnalyzeWithMethodCallFilter_ThenReturnsOnlyTransitiveMethodCallers()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await KnowledgeGraphFixture.SeedAsync(context);
        var analyzer = new ImpactAnalyzer(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await analyzer.Analyze(
            new ImpactAnalysisQuery(
                KnowledgeGraphFixture.TargetFullyQualifiedName.ToLowerInvariant(),
                MaxDepth: 2,
                IncludeTransitive: true,
                IncludedEdgeTypes: [EdgeType.MethodCall]),
            TestContext.Current.CancellationToken);

        Assert.Equal(KnowledgeGraphFixture.TargetFullyQualifiedName, result.TargetSymbol);
        Assert.Collection(
            result.ImpactedNodes,
            node => Assert.Equal(KnowledgeGraphFixture.TransitiveCallerNodeId, node.Id),
            node => Assert.Equal(KnowledgeGraphFixture.DirectCallerNodeId, node.Id));
        Assert.All(result.Dependencies, static edge => Assert.Equal(EdgeType.MethodCall, edge.EdgeType));
    }
}
