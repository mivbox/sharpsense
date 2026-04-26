using Moq;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Abstractions;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.ImpactAnalysis.ImpactAnalysis;

public sealed class ImpactAnalysisQueryHandlerTests
{
    [Fact]
    public void WhenConstructingImpactAnalysisQueryHandler_ThenImplementsQueryHandlerContract()
    {
        var handler = new ImpactAnalysisQueryHandler(new Mock<IImpactAnalyzer>(MockBehavior.Strict).Object);

        Assert.IsAssignableFrom<IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidQuery_ThenInvokesImpactAnalysisService()
    {
        var query = new ImpactAnalysisQuery("node-1");
        var service = new Mock<IImpactAnalyzer>(MockBehavior.Strict);
        service.Setup(analyzer => analyzer.Analyze(query, CancellationToken.None))
            .ReturnsAsync(new ImpactAnalysisResult(query.Identifier, [], []));
        var handler = new ImpactAnalysisQueryHandler(service.Object);

        var result = await handler.Handle(query, CancellationToken.None);

        service.Verify(analyzer => analyzer.Analyze(query, CancellationToken.None), Times.Once);
        Assert.Equal("node-1", result.TargetSymbol);
    }
}
