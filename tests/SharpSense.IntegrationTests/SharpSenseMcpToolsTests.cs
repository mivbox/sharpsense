using AwesomeAssertions;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using SharpSense.Application.CommandExecution.ExecuteProcess.Models;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Memory.DeleteMemory.Models;
using SharpSense.Application.Memory.GetMemories.Models;
using SharpSense.Application.Memory.GetMemory.Models;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Memory.ListMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Errors;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.GetTraceGraph.Models;
using SharpSense.Application.Trace.Models;
using SharpSense.Cli.Mcp;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using Result = FluentResults.Result;

namespace SharpSense.IntegrationTests;

public sealed class SharpSenseMcpToolsTests
{
    [Fact]
    public async Task WhenCtxExecuteSucceeds_ThenItFormatsReducedExecutionOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new Mock<ICommandHandler<ExecuteProcessCommand, Result<CommandExecutionResult>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(
                new ExecuteProcessCommand("dotnet build SharpSense.sln", "Build succeeded"),
                ct))
            .ReturnsAsync(Result.Ok(new CommandExecutionResult(
                "dotnet build SharpSense.sln",
                "/repo",
                "Build succeeded",
                0,
                1,
                1,
                false,
                "Returned 1 merged block(s) from 1 matched line(s) across 1 captured line(s).",
                [new CommandExecutionBlock(1, 1, "1| Build succeeded in 13.7s")])));

        var result = await InvokeTool(
            SharpSenseMcpTools.ctx_execute,
            services => services.AddSingleton(handler.Object),
            new
            {
                command = "dotnet build SharpSense.sln",
                query = "Build succeeded"
            },
            ct);

        result.IsError.Should().NotBe(true);
        result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should().Be(
            "command: dotnet build SharpSense.sln" + Environment.NewLine +
            "status: success" + Environment.NewLine +
            "exit_code: 0" + Environment.NewLine +
            "working_directory: /repo" + Environment.NewLine +
            "query: Build succeeded" + Environment.NewLine +
            "metrics:" + Environment.NewLine +
            "  captured_lines: 1" + Environment.NewLine +
            "  matched_lines: 1" + Environment.NewLine +
            "  block_count: 1" + Environment.NewLine +
            "  truncated: false" + Environment.NewLine +
            "summary: Returned 1 merged block(s) from 1 matched line(s) across 1 captured line(s)." + Environment.NewLine +
            "output:" + Environment.NewLine +
            "  - span: 1-1" + Environment.NewLine +
            "    text: |" + Environment.NewLine +
            "      1| Build succeeded in 13.7s");
    }

    [Fact]
    public async Task WhenCtxExecuteFails_ThenItFormatsExecutionErrorOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new Mock<ICommandHandler<ExecuteProcessCommand, Result<CommandExecutionResult>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(new ExecuteProcessCommand("missing-command", "Error"), ct))
            .ReturnsAsync(Result.Fail<CommandExecutionResult>("Failed to start command 'missing-command'."));

        var result = await InvokeTool(
            SharpSenseMcpTools.ctx_execute,
            services => services.AddSingleton(handler.Object),
            new
            {
                command = "missing-command",
                query = "Error"
            },
            ct);

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should().Be(
            "command: missing-command" + Environment.NewLine +
            "status: error" + Environment.NewLine +
            "error_message: Failed to start command 'missing-command'.");
    }

    [Fact]
    public async Task WhenContextHasMatches_ThenItFormatsCompressedToonOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new Mock<IQueryHandler<GetNodeContextQuery, Result<Context360Result>>>(MockBehavior.Strict);
        var memoryHandler = new Mock<IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(
                It.Is<GetNodeContextQuery>(query => query.NodeId == 42 && query.MaxRelated == 10),
                ct))
            .ReturnsAsync(
                Result.Ok(new Context360Result(
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
                    ],
                    [
                        new Context360RelatedNode(11, "PaymentProcessor")
                    ],
                    [])));

        var result = await InvokeContextTool(handler.Object, memoryHandler.Object, 42, EdgeCategory.Structural, ct);

        result.IsError.Should().NotBe(true);
        result.Content.Should().ContainSingle();
        var text = result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text;
        text.Should().Be(
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
            "  inherits: [IPaymentProcessor (Id:10)]" + Environment.NewLine +
            Environment.NewLine +
            "structural:" + Environment.NewLine +
            "  parents: [PaymentProcessor (Id:11)]" + Environment.NewLine +
            "  children: []");
        handler.Verify(
            candidate => candidate.Handle(
                It.Is<GetNodeContextQuery>(query => query.NodeId == 42 && query.MaxRelated == 10),
                ct),
            Times.Once);
    }

    [Theory]
    [InlineData(42, ServiceErrorCode.NotFound, "No persisted node exists for id 42.")]
    [InlineData(0, ServiceErrorCode.InvalidArgument, "NodeId must be greater than zero.")]
    public async Task WhenContextFails_ThenItReportsAMcpToolErrorWithoutLoadingMemories(
        int nodeId,
        ServiceErrorCode errorCode,
        string message)
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new Mock<IQueryHandler<GetNodeContextQuery, Result<Context360Result>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(new GetNodeContextQuery(nodeId, 10), ct))
            .ReturnsAsync(Result.Fail<Context360Result>(new ServiceError(errorCode, message)));
        var memoryHandler = new Mock<IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>>(MockBehavior.Strict);

        var result = await InvokeContextTool(handler.Object, memoryHandler.Object, nodeId, EdgeCategory.Semantic, ct);

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle();
        var text = result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text;
        text.Should().Be($"context failed: {message}");
        memoryHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenAttachMemorySucceeds_ThenItReturnsCreatedMemoryIdAndStaleState()
    {
        var ct = TestContext.Current.CancellationToken;
        var memoryId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tags = new[] { "security" };
        var handler = new Mock<ICommandHandler<AttachMemoryCommand, Result<MemoryNode>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(
                new AttachMemoryCommand(
                    42,
                    "Security review",
                    tags,
                    Domain.KnowledgeGraph.Enums.MemoryIntent.Convention),
                ct))
            .ReturnsAsync(Result.Ok(new MemoryNode(
                memoryId,
                "Sample",
                "hash",
                "Security review",
                "content-hash",
                ["security"],
                MemoryIntent.Convention,
                DateTimeOffset.UtcNow,
                false)));

        var result = await SharpSenseMcpTools.attach_memory(
            handler.Object,
            42,
            "Security review",
            tags,
            Domain.KnowledgeGraph.Enums.MemoryIntent.Convention,
            ct);

        result.IsError.Should().NotBe(true);
        var text = result.Content
            .OfType<TextContentBlock>()
            .Should().ContainSingle().Which.Text;
        text.Should().Be($"attached memory {memoryId} to node 42 (intent=Convention, stale=false)");
        handler.Verify(
            candidate => candidate.Handle(
                new AttachMemoryCommand(42, "Security review", tags, MemoryIntent.Convention),
                ct),
            Times.Once);
    }

    [Fact]
    public async Task WhenListMemoriesSucceeds_ThenItFormatsCompactMemoryList()
    {
        var ct = TestContext.Current.CancellationToken;
        var memoryId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var handler = new Mock<IQueryHandler<ListMemoriesQuery, Result<ListMemoriesResult>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(
                It.Is<ListMemoriesQuery>(query =>
                    query.StaleOnly &&
                    query.SearchText == "sqlite" &&
                    query.Limit == 25 &&
                    query.Offset == 5 &&
                    query.Intents != null &&
                    query.Intents.SequenceEqual(new[] { MemoryIntent.Invariant })),
                ct))
            .ReturnsAsync(Result.Ok(new ListMemoriesResult(
                [
                    new MemoryNodeSummary(
                        memoryId,
                        42,
                        "Fixture.App.Store.Save",
                        "src/Store.cs",
                        MemoryIntent.Invariant,
                        ["sqlite"],
                        true,
                        DateTimeOffset.Parse("2026-05-14T00:00:00+00:00"),
                        "Keep transactions short.")
                ],
                TotalCount: 1,
                StaleCount: 1,
                HasMore: false)));

        var result = await InvokeTool(
            SharpSenseMcpTools.list_memories,
            services => services.AddSingleton(handler.Object),
            new
            {
                intents = new[] { "Invariant" },
                staleOnly = true,
                searchText = "sqlite",
                limit = 25,
                offset = 5
            },
            ct);

        result.IsError.Should().NotBe(true);
        result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should().Contain(memoryId.ToString());
    }

    [Fact]
    public async Task WhenSemanticSearchHasMatches_ThenItFormatsHierarchicalToonOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var searchHandler = new Mock<IQueryHandler<HybridSearchQuery, Result<HybridSearchResult>>>(MockBehavior.Strict);
        searchHandler
            .Setup(candidate => candidate.Handle(
                It.Is<HybridSearchQuery>(query =>
                    query.SearchText == "graph node" &&
                    query.Limit == 3 &&
                    query.ProjectId == "SharpSense.Infrastructure" &&
                    query.IncludeMemories &&
                    query.IncludedNodeTypes != null &&
                    query.IncludedNodeTypes.SequenceEqual(new[] { NodeType.Method }) &&
                    query.TagFilters != null &&
                    query.TagFilters.SequenceEqual(new[] { "graph" })),
                ct))
            .ReturnsAsync(
                Result.Ok(new HybridSearchResult(
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
                    ])));

        var result = await SharpSenseMcpTools.semantic_search(
            searchHandler.Object,
            "graph node",
            3,
            "SharpSense.Infrastructure",
            [NodeType.Method],
            includeMemories: true,
            tags: ["graph"],
            ct: ct);

        result.IsError.Should().NotBe(true);
        result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should().Be(
            "src/SharpSense.Infrastructure/DependencyGraph/:" + Environment.NewLine +
            "  DependencyGraphMapper.cs:" + Environment.NewLine +
            "    - [M] `553` ToExternalGraphNode L20-21" + Environment.NewLine +
            "    - [M] `556` ToGraphNode L32-47" + Environment.NewLine +
            Environment.NewLine +
            "src/SharpSense.Domain/KnowledgeGraph/Nodes/:" + Environment.NewLine +
            "  ProjectNode.cs:" + Environment.NewLine +
            "    - [P] `373` Id L5");
    }

    [Fact]
    public async Task WhenGetInheritorsHasMatches_ThenItFormatsDerivedClassesAsToonOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var inheritorsHandler = new Mock<IQueryHandler<GetInheritorsQuery, CodeNodeResult[]>>(MockBehavior.Strict);
        var traceNavigator = new Mock<ITraceNavigator>(MockBehavior.Strict);
        traceNavigator
            .Setup(candidate => candidate.GetRootNode("42", ct))
            .ReturnsAsync(new CodeNodeResult(42, "node-base", "project-app", "Fixture.App.Base", "Base", NodeType.Interface, "src/Base.cs", 1, 5, ""));
        inheritorsHandler
            .Setup(candidate => candidate.Handle(
                new GetInheritorsQuery(42),
                ct))
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
            traceNavigator.Object,
            42,
            ct: ct);

        result.IsError.Should().NotBe(true);
        result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should().Be(
            "[C] `7` DerivedAlpha @ src/Fixture.App/DerivedAlpha.cs:3-16" + Environment.NewLine +
            "[C] `8` DerivedBeta @ src/Fixture.App/DerivedBeta.cs:3-17");
        inheritorsHandler.Verify(
            candidate => candidate.Handle(new GetInheritorsQuery(42), ct),
            Times.Once);
    }

    [Fact]
    public async Task WhenGetInheritorsTargetIsMissing_ThenProtocolMarksToolResultAsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var inheritorsHandler = new Mock<IQueryHandler<GetInheritorsQuery, CodeNodeResult[]>>(MockBehavior.Strict);
        var traceNavigator = new Mock<ITraceNavigator>(MockBehavior.Strict);
        traceNavigator
            .Setup(candidate => candidate.GetRootNode("404", ct))
            .ReturnsAsync((CodeNodeResult?)null);

        var result = await SharpSenseMcpTools.get_inheritors(
            inheritorsHandler.Object,
            traceNavigator.Object,
            404,
            ct);

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be("get_inheritors failed: No persisted node exists for id 404.");
        inheritorsHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenTraceNodeUsesDefaultDirection_ThenItDelegatesToTraceGraphWithDepthOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var traceGraphHandler = new Mock<IQueryHandler<GetTraceGraphQuery, Result<TraceGraphResult>>>(MockBehavior.Strict);
        var memoryReader = new Mock<IMemoryRepository>(MockBehavior.Strict);
        var root = new CodeNodeResult(
            42,
            "node-root",
            "project-app",
            "Fixture.App.MessageConsumer.Render()",
            "MessageConsumer.Render()",
            NodeType.Method,
            "src/Fixture.App/MessageConsumer.cs",
            20,
            28,
            "Renders a message.");
        var callee = new CodeNodeResult(
            1,
            "node-callee",
            "project-app",
            "Fixture.App.MessageProvider.GetMessage()",
            "MessageProvider.GetMessage()",
            NodeType.Method,
            "src/Fixture.App/MessageProvider.cs",
            7,
            11,
            "Gets a message.");
        traceGraphHandler
            .Setup(candidate => candidate.Handle(
                new GetTraceGraphQuery(42, TraceDirection.Callee, 1, null),
                ct))
            .ReturnsAsync(Result.Ok(new TraceGraphResult(
                root,
                TraceDirection.Callee,
                [callee],
                [new ImpactedDependencyEdge("node-root", "node-callee", EdgeType.MethodCall)])));

        var result = await SharpSenseMcpTools.trace_node(
            traceGraphHandler.Object,
            memoryReader.Object,
            42,
            ct: ct);

        result.IsError.Should().NotBe(true);
        result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should().Be(
            "- [M] `42` MessageConsumer.Render @ src/Fixture.App/MessageConsumer.cs:L20-28" + Environment.NewLine +
            "  -> [M] `1` MessageProvider.GetMessage @ src/Fixture.App/MessageProvider.cs:L7-11");
    }

    [Fact]
    public async Task WhenTraceNodeTargetIsMissing_ThenProtocolMarksToolResultAsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var traceGraphHandler = new Mock<IQueryHandler<GetTraceGraphQuery, Result<TraceGraphResult>>>(MockBehavior.Strict);
        var memoryReader = new Mock<IMemoryRepository>(MockBehavior.Strict);
        traceGraphHandler
            .Setup(candidate => candidate.Handle(
                new GetTraceGraphQuery(999, TraceDirection.Caller, 3, null),
                ct))
            .ReturnsAsync(Result.Fail<TraceGraphResult>(new ServiceError(
                ServiceErrorCode.NotFound,
                "No indexed code node exists for id 999.")));

        var result = await InvokeTool(
            SharpSenseMcpTools.trace_node,
            services =>
            {
                services.AddSingleton(traceGraphHandler.Object);
                services.AddSingleton(memoryReader.Object);
            },
            new
            {
                nodeId = 999,
                direction = "caller"
            },
            ct);

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be("trace_node failed: No indexed code node exists for id 999.");
    }

    [Fact]
    public async Task WhenAttachMemoryFails_ThenProtocolMarksToolResultAsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var error = new ServiceError(ServiceErrorCode.InvalidArgument, "Invalid request.");
        var handler = new Mock<ICommandHandler<AttachMemoryCommand, Result<MemoryNode>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(
                It.Is<AttachMemoryCommand>(command =>
                    command.NodeId == 42 &&
                    command.Content == "note" &&
                    command.Intent == MemoryIntent.Convention),
                ct))
            .ReturnsAsync(Result.Fail<MemoryNode>(error));

        var response = await InvokeTool(
            SharpSenseMcpTools.attach_memory,
            services => services.AddSingleton(handler.Object),
            new
            {
                nodeId = 42,
                content = "note"
            },
            ct);

        response.IsError.Should().BeTrue();
        response.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be("attach_memory failed: Invalid request.");
    }

    [Fact]
    public async Task WhenDeleteMemoryFails_ThenProtocolMarksToolResultAsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var memoryId = Guid.NewGuid();
        var error = new ServiceError(ServiceErrorCode.InvalidArgument, "Invalid request.");
        var handler = new Mock<ICommandHandler<DeleteMemoryCommand, Result>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(It.Is<DeleteMemoryCommand>(command => command.MemoryId == memoryId), ct))
            .ReturnsAsync(Result.Fail(error));

        var response = await InvokeTool(
            SharpSenseMcpTools.delete_memory,
            services => services.AddSingleton(handler.Object),
            new
            {
                memoryId
            },
            ct);

        response.IsError.Should().BeTrue();
        response.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be("delete_memory failed: Invalid request.");
    }

    [Fact]
    public async Task WhenGetMemoryFails_ThenProtocolMarksToolResultAsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var memoryId = Guid.NewGuid();
        var error = new ServiceError(ServiceErrorCode.InvalidArgument, "Invalid request.");
        var handler = new Mock<IQueryHandler<GetMemoryQuery, Result<MemoryNode>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(It.Is<GetMemoryQuery>(query => query.MemoryId == memoryId), ct))
            .ReturnsAsync(Result.Fail<MemoryNode>(error));

        var response = await InvokeTool(
            SharpSenseMcpTools.get_memory,
            services => services.AddSingleton(handler.Object),
            new
            {
                memoryId
            },
            ct);

        response.IsError.Should().BeTrue();
        response.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be("get_memory failed: Invalid request.");
    }

    [Fact]
    public async Task WhenGetMemoriesFails_ThenProtocolMarksToolResultAsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var memoryId = Guid.NewGuid();
        var error = new ServiceError(ServiceErrorCode.InvalidArgument, "Invalid request.");
        var handler = new Mock<IQueryHandler<GetMemoriesQuery, Result<IReadOnlyDictionary<Guid, MemoryNode>>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(It.Is<GetMemoriesQuery>(query => query.MemoryIds.SequenceEqual(new[] { memoryId })), ct))
            .ReturnsAsync(Result.Fail<IReadOnlyDictionary<Guid, MemoryNode>>(error));

        var response = await InvokeTool(
            SharpSenseMcpTools.get_memories,
            services => services.AddSingleton(handler.Object),
            new
            {
                memoryIds = new[] { memoryId }
            },
            ct);

        response.IsError.Should().BeTrue();
        response.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be("get_memories failed: Invalid request.");
    }

    [Fact]
    public async Task WhenSemanticSearchFails_ThenProtocolMarksToolResultAsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var error = new ServiceError(ServiceErrorCode.InvalidArgument, "Invalid request.");
        var handler = new Mock<IQueryHandler<HybridSearchQuery, Result<HybridSearchResult>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(It.Is<HybridSearchQuery>(query => query.SearchText == ""), ct))
            .ReturnsAsync(Result.Fail<HybridSearchResult>(error));

        var response = await InvokeTool(
            SharpSenseMcpTools.semantic_search,
            services => services.AddSingleton(handler.Object),
            new
            {
                query = ""
            },
            ct);

        response.IsError.Should().BeTrue();
        response.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be("semantic_search failed: Invalid request.");
    }

    [Fact]
    public async Task WhenContextMemoryLookupFails_ThenReturnsStructuralContextWithoutThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new Mock<IQueryHandler<GetNodeContextQuery, Result<Context360Result>>>(MockBehavior.Strict);
        var memoryHandler = new Mock<IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(
                It.Is<GetNodeContextQuery>(query => query.NodeId == 42 && query.MaxRelated == 10),
                ct))
            .ReturnsAsync(Result.Ok(new Context360Result(
                new Context360Node(42, "Example.Run()", NodeType.Method, "src/Example.cs", 1, 10),
                [],
                [],
                [],
                [],
                [],
                [])));
        memoryHandler
            .Setup(candidate => candidate.Handle(
                It.Is<GetNodeMemoriesQuery>(query => query.NodeId == 42),
                ct))
            .ReturnsAsync(Result.Fail<MemoryNode[]>("Node disappeared."));

        var response = await InvokeContextTool(handler.Object, memoryHandler.Object, 42, EdgeCategory.All, ct);

        response.IsError.Should().NotBe(true);
        response.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().NotContain("semantic_context:");
    }

    private static Task<CallToolResult> InvokeContextTool(
        IQueryHandler<GetNodeContextQuery, Result<Context360Result>> handler,
        IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>> memoryHandler,
        int nodeId,
        EdgeCategory edgeCategories,
        CancellationToken ct)
        => InvokeTool(
            SharpSenseMcpTools.context,
            services =>
            {
                services.AddSingleton(handler);
                services.AddSingleton(memoryHandler);
            },
            new
            {
                nodeId,
                edgeCategories
            },
            ct);

    private static async Task<CallToolResult> InvokeTool(
        Delegate method,
        Action<IServiceCollection> configure,
        object arguments,
        CancellationToken ct)
    {
        var services = new ServiceCollection();
        configure(services);
        await using var provider = services.BuildServiceProvider();
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        await using var server = McpServer.Create(transport, new McpServerOptions(), null, provider);
        var serializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        serializerOptions.Converters.Add(new JsonStringEnumConverter<EdgeCategory>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<EdgeType>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<MemoryIntent>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<NodeType>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<ContextSection>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<TraceDirection>());
        var tool = McpServerTool.Create(
            method,
            new McpServerToolCreateOptions
            {
                Services = provider,
                SerializerOptions = serializerOptions
            });
        var parameters = new CallToolRequestParams
        {
            Name = tool.ProtocolTool.Name,
            Arguments = JsonSerializer.SerializeToElement(arguments, serializerOptions)
                .EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value)
        };
        var request = new RequestContext<CallToolRequestParams>(
            server,
            new JsonRpcRequest
            {
                Id = new RequestId(1),
                Method = "tools/call"
            },
            parameters)
        {
            Services = provider
        };

        return await tool.InvokeAsync(request, ct);
    }
}
