using AwesomeAssertions;
using Moq;
using SharpSense.Application.ImpactAnalysis.Abstractions;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Errors;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.GetTraceGraph;
using SharpSense.Application.Trace.GetTraceGraph.Models;
using SharpSense.Application.Trace.Models;
using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Trace.GetTraceGraph;

public sealed class GetTraceGraphQueryHandlerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task WhenCalleesContainCycles_ThenItVisitsEachNodeOnceWithinTheRequestedDepth(int maxDepth)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Node(1);
        var child = Node(2);
        var grandchild = Node(3);
        var navigator = new Mock<ITraceNavigator>(MockBehavior.Strict);
        navigator
            .Setup(candidate => candidate.GetRootNode("1", ct))
            .ReturnsAsync(root);
        navigator
            .Setup(candidate => candidate.GetCallees(new TraceQuery("1"), ct))
            .ReturnsAsync([child, child]);
        navigator
            .Setup(candidate => candidate.GetCallees(new TraceQuery("2"), ct))
            .ReturnsAsync([root, grandchild]);
        navigator
            .Setup(candidate => candidate.GetCallees(new TraceQuery("3"), ct))
            .ReturnsAsync([child]);
        var expectedNodes = maxDepth == 1 ? new[] { child } : [child, grandchild];
        var edge = new ImpactedDependencyEdge(root.CanonicalId, child.CanonicalId, EdgeType.MethodCall);
        navigator
            .Setup(candidate => candidate.GetDependencies(
                It.Is<IReadOnlyCollection<CodeNodeResult>>(nodes => nodes.Count == expectedNodes.Length + 1 && nodes.Contains(root)),
                ct))
            .ReturnsAsync([edge]);
        var impactAnalyzer = new Mock<IImpactAnalyzer>(MockBehavior.Strict);
        var handler = new GetTraceGraphQueryHandler(navigator.Object, impactAnalyzer.Object);

        var result = await handler.Handle(new GetTraceGraphQuery(1, TraceDirection.Callee, maxDepth), ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Root.Should().Be(root);
        result.Value.Nodes.Should().Equal(expectedNodes);
        result.Value.Dependencies.Should().Equal(edge);
        result.Value.Truncated.Should().BeFalse();
        navigator.Verify(candidate => candidate.GetCallees(new TraceQuery("1"), ct), Times.Once);
        navigator.Verify(
            candidate => candidate.GetCallees(
                new TraceQuery("2"),
                ct),
            maxDepth > 1 ? Times.Once() : Times.Never());
        navigator.Verify(
            candidate => candidate.GetCallees(
                new TraceQuery("3"),
                ct),
            maxDepth > 2 ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    public async Task WhenCalleeCountReachesTheBudget_ThenItBoundsTheGraphAndReportsOnlyOmittedNodesAsTruncated(
        int calleeCount,
        bool truncated)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Node(1);
        var callees = Enumerable.Range(2, calleeCount)
            .Select(Node)
            .ToArray();
        var navigator = new Mock<ITraceNavigator>(MockBehavior.Strict);
        navigator
            .Setup(candidate => candidate.GetRootNode("1", ct))
            .ReturnsAsync(root);
        navigator
            .Setup(candidate => candidate.GetCallees(new TraceQuery("1"), ct))
            .ReturnsAsync(callees);
        navigator
            .Setup(candidate => candidate.GetDependencies(
                It.Is<IReadOnlyCollection<CodeNodeResult>>(nodes => nodes.Count == 1000 && nodes.All(node => node.Id <= 1000)),
                ct))
            .ReturnsAsync([]);
        var handler = new GetTraceGraphQueryHandler(navigator.Object, Mock.Of<IImpactAnalyzer>());

        var result = await handler.Handle(new GetTraceGraphQuery(1, TraceDirection.Callee, 1), ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Nodes.Should().Equal(callees.Take(999));
        result.Value.Truncated.Should().Be(truncated);
    }

    [Fact]
    public async Task WhenTracingCallers_ThenItPreservesImpactNodesDependenciesAndDepth()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Node(1);
        var caller = new ImpactedCodeNode(
            2,
            "code:2",
            "project",
            "Sample.Caller",
            "Caller",
            NodeType.Method,
            "Caller.cs",
            10,
            20,
            "Caller summary");
        var dependency = new ImpactedDependencyEdge(caller.CanonicalId, root.CanonicalId, EdgeType.MethodCall);
        var navigator = new Mock<ITraceNavigator>(MockBehavior.Strict);
        navigator
            .Setup(candidate => candidate.GetRootNode("1", ct))
            .ReturnsAsync(root);
        var impactAnalyzer = new Mock<IImpactAnalyzer>(MockBehavior.Strict);
        impactAnalyzer
            .Setup(candidate => candidate.Analyze(new ImpactAnalysisQuery("1", 4), ct))
            .ReturnsAsync(new ImpactAnalysisResult("1", [caller], [dependency]));
        var handler = new GetTraceGraphQueryHandler(navigator.Object, impactAnalyzer.Object);

        var result = await handler.Handle(new GetTraceGraphQuery(1, TraceDirection.Caller, 4), ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Root.Should().Be(root);
        result.Value.Direction.Should().Be(TraceDirection.Caller);
        result.Value.Nodes.Should().Equal(new CodeNodeResult(
            2,
            "code:2",
            "project",
            "Sample.Caller",
            "Caller",
            NodeType.Method,
            "Caller.cs",
            10,
            20,
            "Caller summary"));
        result.Value.Dependencies.Should().Equal(dependency);
        result.Value.Truncated.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, TraceDirection.Callee, 3)]
    [InlineData(1, TraceDirection.Callee, 0)]
    [InlineData(1, TraceDirection.Caller, 11)]
    [InlineData(1, (TraceDirection)99, 3)]
    public async Task WhenTheQueryIsInvalid_ThenItFailsBeforeReadingTheGraph(
        int nodeId,
        TraceDirection direction,
        int maxDepth)
    {
        var navigator = new Mock<ITraceNavigator>(MockBehavior.Strict);
        var impactAnalyzer = new Mock<IImpactAnalyzer>(MockBehavior.Strict);
        var handler = new GetTraceGraphQueryHandler(navigator.Object, impactAnalyzer.Object);

        var result = await handler.Handle(
            new GetTraceGraphQuery(nodeId, direction, maxDepth),
            TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors
            .OfType<ServiceError>()
            .Should().ContainSingle().Which;
        error.ErrorCode.Should().Be(ServiceErrorCode.InvalidArgument);
        navigator.VerifyNoOtherCalls();
        impactAnalyzer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenTheRootIsMissing_ThenItReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var navigator = new Mock<ITraceNavigator>(MockBehavior.Strict);
        navigator
            .Setup(candidate => candidate.GetRootNode("1", ct))
            .ReturnsAsync((CodeNodeResult?)null);
        var handler = new GetTraceGraphQueryHandler(navigator.Object, Mock.Of<IImpactAnalyzer>());

        var result = await handler.Handle(new GetTraceGraphQuery(1), ct);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors
            .OfType<ServiceError>()
            .Should().ContainSingle().Which;
        error.ErrorCode.Should().Be(ServiceErrorCode.NotFound);
    }

    [Fact]
    public async Task WhenTraversalIsCancelled_ThenCancellationPropagates()
    {
        var ct = TestContext.Current.CancellationToken;
        var navigator = new Mock<ITraceNavigator>(MockBehavior.Strict);
        navigator
            .Setup(candidate => candidate.GetRootNode("1", ct))
            .ReturnsAsync(Node(1));
        navigator
            .Setup(candidate => candidate.GetCallees(new TraceQuery("1"), ct))
            .ThrowsAsync(new OperationCanceledException(ct));
        var handler = new GetTraceGraphQueryHandler(navigator.Object, Mock.Of<IImpactAnalyzer>());

        var act = () => handler.Handle(new GetTraceGraphQuery(1), ct);

        await act.Should().ThrowExactlyAsync<OperationCanceledException>();
    }

    private static CodeNodeResult Node(int id) => new(
        id,
        $"code:{id}",
        "project",
        $"Sample.Node{id}",
        $"Node{id}",
        NodeType.Method,
        "Sample.cs",
        1,
        5,
        "Summary");
}
