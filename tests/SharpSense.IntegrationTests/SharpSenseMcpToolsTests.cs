using AwesomeAssertions;
using Moq;
using SharpSense.Application.Features.ImpactAnalysis.Contracts;
using SharpSense.Application.Features.ImpactAnalysis.ImpactAnalysis;
using SharpSense.Application.Features.Trace;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Cli.Mcp;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.IntegrationTests;

public sealed class SharpSenseMcpToolsTests
{
    [Fact]
    public async Task WhenTraceNodeUsesDefaultDirection_ThenItCallsTheCalleeNavigator()
    {
        var impactHandler = new Mock<IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult>>(MockBehavior.Strict);
        var traceHandler = new Mock<IQueryHandler<TraceQuery, CodeNodeResult[]>>(MockBehavior.Strict);
        traceHandler.Setup(candidate => candidate.Handle(new TraceQuery("node-root"), CancellationToken.None))
            .ReturnsAsync(
            [
                new CodeNodeResult(
                    1,
                    "node-callee",
                    "project-app",
                    "Fixture.App.MessageProvider.GetMessage()",
                    "MessageProvider.GetMessage()",
                    NodeType.Method,
                    "src/Fixture.App/MessageProvider.cs",
                    7,
                    11,
                    "Gets a message.")
            ]);

        var result = await SharpSenseMcpTools.trace_node(
            impactHandler.Object,
            traceHandler.Object,
            "node-root",
            ct: CancellationToken.None);

        result.Should().Be("[M] `1` MessageProvider.GetMessage() @ src/Fixture.App/MessageProvider.cs:7-11");
        traceHandler.Verify(candidate => candidate.Handle(new TraceQuery("node-root"), CancellationToken.None), Times.Once);
        impactHandler.VerifyNoOtherCalls();
    }
}
