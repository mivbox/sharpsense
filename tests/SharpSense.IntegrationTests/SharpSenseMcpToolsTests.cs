using AwesomeAssertions;
using Moq;
using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
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
    public async Task WhenContextHasMatches_ThenItFormatsCompressedToonOutput()
    {
        var contextService = new Mock<IContextService>(MockBehavior.Strict);
        contextService.Setup(candidate => candidate.GetNodeContext(42, 10, CancellationToken.None))
            .ReturnsAsync(
                new Context360Result(
                    new Context360Node(
                        42,
                        "PaymentProcessor.ProcessPayment(string, int)",
                        NodeType.Method,
                        "src/Fixture.App/PaymentProcessor.cs",
                        12,
                        30),
                    [
                        new Context360RelatedNode(7, "HttpEndpoint.Handle")
                    ],
                    [
                        new Context360RelatedNode(8, "PaymentProcessorBase")
                    ],
                    [
                        new Context360RelatedNode(9, "ReceiptWriter.WriteReceipt")
                    ],
                    [
                        new Context360RelatedNode(10, "IPaymentProcessor")
                    ]));

        var result = await SharpSenseMcpTools.context(
            contextService.Object,
            42,
            CancellationToken.None);

        result.Should().Be(
            "node:" + Environment.NewLine +
            "  id: 42" + Environment.NewLine +
            "  name: PaymentProcessor.ProcessPayment(string, int)" + Environment.NewLine +
            "  kind: M" + Environment.NewLine +
            "  file: src/Fixture.App/PaymentProcessor.cs:12-30" + Environment.NewLine +
            Environment.NewLine +
            "incoming:" + Environment.NewLine +
            "  callers: [HttpEndpoint.Handle (Id:7)]" + Environment.NewLine +
            "  implementers: [PaymentProcessorBase (Id:8)]" + Environment.NewLine +
            Environment.NewLine +
            "outgoing:" + Environment.NewLine +
            "  callees: [ReceiptWriter.WriteReceipt (Id:9)]" + Environment.NewLine +
            "  inherits: [IPaymentProcessor (Id:10)]");
        contextService.Verify(candidate => candidate.GetNodeContext(42, 10, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task WhenSemanticSearchHasMatches_ThenItFormatsHierarchicalToonOutput()
    {
        var searchHandler = new Mock<IQueryHandler<HybridSearchQuery, HybridSearchResult>>(MockBehavior.Strict);
        searchHandler.Setup(candidate => candidate.Handle(new HybridSearchQuery("graph node", 3), CancellationToken.None))
            .ReturnsAsync(
                new HybridSearchResult(
                    "graph node",
                    [
                        new HybridSearchHit(
                            553,
                            "node-dependency-graph-to-external",
                            "project-app",
                            "Fixture.DependencyGraphMapper.ToExternalGraphNode(ProjectNode, GraphNode)",
                            "ToExternalGraphNode(ProjectNode, GraphNode)",
                            NodeType.Method,
                            "src/SharpSense.Infrastructure/DependencyGraph/DependencyGraphMapper.cs",
                            20,
                            21,
                            "Maps a project node."),
                        new HybridSearchHit(
                            556,
                            "node-dependency-graph-to-graph",
                            "project-app",
                            "Fixture.DependencyGraphMapper.ToGraphNode(GraphNode)",
                            "ToGraphNode(GraphNode)",
                            NodeType.Method,
                            "src/SharpSense.Infrastructure/DependencyGraph/DependencyGraphMapper.cs",
                            32,
                            47,
                            "Maps a graph node."),
                        new HybridSearchHit(
                            373,
                            "node-project-id",
                            "project-app",
                            "Fixture.ProjectNode.Id",
                            "Id",
                            NodeType.Property,
                            "src/SharpSense.Domain/KnowledgeGraph/Nodes/ProjectNode.cs",
                            5,
                            5,
                            "Project identifier.")
                    ]));

        var result = await SharpSenseMcpTools.semantic_search(
            searchHandler.Object,
            "graph node",
            3,
            CancellationToken.None);

        result.Should().Be(
            "src/SharpSense.Infrastructure/DependencyGraph/:" + Environment.NewLine +
            "  DependencyGraphMapper.cs:" + Environment.NewLine +
            "    - [M] `553` ToExternalGraphNode L20-21" + Environment.NewLine +
            "    - [M] `556` ToGraphNode L32-47" + Environment.NewLine +
            Environment.NewLine +
            "src/SharpSense.Domain/KnowledgeGraph/Nodes/:" + Environment.NewLine +
            "  ProjectNode.cs:" + Environment.NewLine +
            "    - [P] `373` Id L5");
        searchHandler.Verify(candidate => candidate.Handle(new HybridSearchQuery("graph node", 3), CancellationToken.None), Times.Once);
    }

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
