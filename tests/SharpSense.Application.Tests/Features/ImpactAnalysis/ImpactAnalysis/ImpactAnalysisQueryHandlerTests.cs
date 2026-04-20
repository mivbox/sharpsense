using SharpSense.Application.Features.ImpactAnalysis.Contracts;
using SharpSense.Application.Features.ImpactAnalysis.ImpactAnalysis;
using SharpSense.Application.Features.ImpactAnalysis.Infrastructure;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.Features.ImpactAnalysis.ImpactAnalysis;

public sealed class ImpactAnalysisQueryHandlerTests
{
    [Fact]
    public void WhenConstructingImpactAnalysisQueryHandler_ThenImplementsQueryHandlerContract()
    {
        var handler = new ImpactAnalysisQueryHandler(new FakeImpactAnalysisService());

        Assert.IsAssignableFrom<IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidQuery_ThenInvokesImpactAnalysisService()
    {
        var service = new FakeImpactAnalysisService();
        var handler = new ImpactAnalysisQueryHandler(service);
        var query = new ImpactAnalysisQuery("node-1");

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Same(query, service.LastQuery);
        Assert.Equal("node-1", result.TargetSymbol);
    }

    private sealed class FakeImpactAnalysisService : IImpactAnalysisService
    {
        public ImpactAnalysisQuery? LastQuery { get; private set; }

        public Task<ImpactAnalysisResult> Analyze(ImpactAnalysisQuery query, CancellationToken ct)
        {
            LastQuery = query;
            return Task.FromResult(new ImpactAnalysisResult(query.Identifier, [], []));
        }
    }
}
