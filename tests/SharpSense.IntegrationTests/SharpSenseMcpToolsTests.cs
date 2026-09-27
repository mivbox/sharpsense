using AwesomeAssertions;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.ExecuteProcess;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Errors;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.Trace.Models;
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
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory.Setup(candidate => candidate.Create(CancellationToken.None))
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex.Setup(candidate => candidate.AppendLine("Build succeeded in 13.7s", CancellationToken.None))
            .ReturnsAsync(Result.Ok(1));
        executeLogIndex.Setup(candidate => candidate.FindMatches("Build succeeded", CancellationToken.None))
            .ReturnsAsync(Result.Ok<int[]>([1]));
        executeLogIndex.Setup(candidate => candidate.ReadRange(new ExecutionLineRange(1, 1), CancellationToken.None))
            .ReturnsAsync(Result.Ok<ExecutionLogLine[]>(
            [
                new ExecutionLogLine(1, "Build succeeded in 13.7s")
            ]));
        executeLogIndex.Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner.Setup(candidate => candidate.Execute(
            It.Is<CommandProcessRequest>(request =>
                    request.Command == "dotnet build SharpSense.sln" &&
                    request.WorkingDirectory == "/repo"),
            It.IsAny<Func<string, CancellationToken, Task>>(),
            CancellationToken.None))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> onOutput,
                CancellationToken innerCt) =>
            {
                await onOutput("Build succeeded in 13.7s", innerCt);

                return Result.Ok(new CommandProcessResult(0));
            });

        var result = await SharpSenseMcpTools.ctx_execute(
            new ExecuteProcessCommandHandler(
                processRunner.Object,
                executeLogIndexFactory.Object,
                Options.Create(new WorkspaceExecutionOptions
                {
                    RepositoryRoot = "/repo"
                })),
            "dotnet build SharpSense.sln",
            "Build succeeded",
            CancellationToken.None);

        result.Should().Be(
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
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory.Setup(candidate => candidate.Create(CancellationToken.None))
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex.Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner.Setup(candidate => candidate.Execute(
            It.Is<CommandProcessRequest>(request =>
                    request.Command == "missing-command" &&
                    request.WorkingDirectory == "/repo"),
            It.IsAny<Func<string, CancellationToken, Task>>(),
            CancellationToken.None))
            .ReturnsAsync(Result.Fail<CommandProcessResult>("Failed to start command 'missing-command'."));

        var result = await SharpSenseMcpTools.ctx_execute(
            new ExecuteProcessCommandHandler(
                processRunner.Object,
                executeLogIndexFactory.Object,
                Options.Create(new WorkspaceExecutionOptions
                {
                    RepositoryRoot = "/repo"
                })),
            "missing-command",
            "Error",
            CancellationToken.None);

        result.Should().Be(
            "command: missing-command" + Environment.NewLine +
            "status: error" + Environment.NewLine +
            "error_message: Failed to start command 'missing-command'.");
    }

    [Fact]
    public async Task WhenContextHasMatches_ThenItFormatsCompressedToonOutput()
    {
        var handler = new Mock<IQueryHandler<GetNodeContextQuery, Result<Context360Result>>>(MockBehavior.Strict);
        var memoryHandler = new Mock<IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>>(MockBehavior.Strict);
        handler.Setup(candidate => candidate.Handle(
            It.Is<GetNodeContextQuery>(query => query.NodeId == 42 && query.MaxRelated == 10),
            TestContext.Current.CancellationToken))
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

        var result = await InvokeContextTool(
            handler.Object,
            memoryHandler.Object,
            42,
            EdgeCategory.Structural,
            TestContext.Current.CancellationToken);

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
                TestContext.Current.CancellationToken),
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
    public async Task WhenAttachMemorySucceeds_ThenItReturnsConciseSuccessMessage()
    {
        var tags = new[]
        {
            "security"
        };
        var handler = new Mock<ICommandHandler<AttachMemoryCommand, Result>>(MockBehavior.Strict);
        handler.Setup(candidate => candidate.Handle(
            new AttachMemoryCommand(42, "Security review", tags, SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent.Convention),
            CancellationToken.None))
            .ReturnsAsync(Result.Ok());

        var result = await SharpSenseMcpTools.attach_memory(
            handler.Object,
            42,
            "Security review",
            tags,
            SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent.Convention,
            CancellationToken.None);

        result.Should().Be("attached memory to node 42 (intent=Convention)");
        handler.VerifyAll();
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
        var memoryReader = new Mock<IMemoryRepository>(MockBehavior.Strict);
        var traceNavigator = new Mock<ITraceNavigator>(MockBehavior.Strict);
        traceNavigator.Setup(candidate => candidate.GetRootNode("node-root", CancellationToken.None))
            .ReturnsAsync(
                new CodeNodeResult(
                    42,
                    "node-root",
                    "project-app",
                    "Fixture.App.MessageConsumer.Render()",
                    "MessageConsumer.Render()",
                    NodeType.Method,
                    "src/Fixture.App/MessageConsumer.cs",
                    20,
                    28,
                    "Renders a message."));
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
            memoryReader.Object,
            traceNavigator.Object,
            "node-root",
            ct: CancellationToken.None);

        result.Should().Be(
            "- [M] `42` MessageConsumer.Render @ src/Fixture.App/MessageConsumer.cs:L20-28" + Environment.NewLine +
            "  -> [M] `1` MessageProvider.GetMessage @ src/Fixture.App/MessageProvider.cs:L7-11");
        traceNavigator.Verify(candidate => candidate.GetRootNode("node-root", CancellationToken.None), Times.Once);
        traceHandler.Verify(candidate => candidate.Handle(new TraceQuery("node-root"), CancellationToken.None), Times.Once);
        impactHandler.VerifyNoOtherCalls();
    }

    private static async Task<CallToolResult> InvokeContextTool(
        IQueryHandler<GetNodeContextQuery, Result<Context360Result>> handler,
        IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>> memoryHandler,
        int nodeId,
        EdgeCategory edgeCategories,
        CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        services.AddSingleton(memoryHandler);
        await using var provider = services.BuildServiceProvider();
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        await using var server = McpServer.Create(transport, new McpServerOptions(), null, provider);
        var serializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        serializerOptions.Converters.Add(new JsonStringEnumConverter<EdgeCategory>());
        var tool = McpServerTool.Create(
            SharpSenseMcpTools.context,
            new McpServerToolCreateOptions
            {
                Services = provider,
                SerializerOptions = serializerOptions
            });
        var parameters = new CallToolRequestParams
        {
            Name = "context",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["nodeId"] = JsonSerializer.SerializeToElement(nodeId),
                ["edgeCategories"] = JsonSerializer.SerializeToElement(edgeCategories, serializerOptions)
            }
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
