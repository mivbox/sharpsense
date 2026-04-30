using AwesomeAssertions;
using Moq;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Cli.Mcp;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.IntegrationTests;

public sealed class SharpSenseMcpToolsTests
{
    [Fact]
    public async Task WhenGetInheritorsHasMatches_ThenItFormatsDerivedClassesAsToonOutput()
    {
        var inheritorsHandler = new Mock<IQueryHandler<GetInheritorsQuery, CodeNodeResult[]>>(MockBehavior.Strict);
        inheritorsHandler.Setup(candidate => candidate.Handle(new GetInheritorsQuery(42), CancellationToken.None))
            .ReturnsAsync(
            [
                new CodeNodeResult(
                    7,
                    "node-derived-alpha",
                    "project-app",
                    "Fixture.App.DerivedAlpha",
                    "DerivedAlpha",
                    NodeType.Class,
                    "src/Fixture.App/DerivedAlpha.cs",
                    3,
                    16,
                    "Derived alpha."),
                new CodeNodeResult(
                    8,
                    "node-derived-beta",
                    "project-app",
                    "Fixture.App.DerivedBeta",
                    "DerivedBeta",
                    NodeType.Class,
                    "src/Fixture.App/DerivedBeta.cs",
                    3,
                    17,
                    "Derived beta.")
            ]);

        var result = await SharpSenseMcpTools.get_inheritors(
            inheritorsHandler.Object,
            42,
            ct: CancellationToken.None);

        result.Should().Be(
            "[C] `7` DerivedAlpha @ src/Fixture.App/DerivedAlpha.cs:3-16" + Environment.NewLine +
            "[C] `8` DerivedBeta @ src/Fixture.App/DerivedBeta.cs:3-17");
        inheritorsHandler.Verify(candidate => candidate.Handle(new GetInheritorsQuery(42), CancellationToken.None), Times.Once);
    }

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
