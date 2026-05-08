using SharpSense.Application.Context360.Models;
using AwesomeAssertions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Cli.Shared;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.IntegrationTests;

public sealed class TokenObjectNotationTests
{
    [Fact]
    public void WhenSerializeContext360HasMixedBreadth_ThenItFormatsCompressedToonOutput()
    {
        var result = new Context360Result(
            new Context360Node(
                1,
                "MessageConsumer.Render()",
                NodeType.Method,
                "src/Fixture.App/MessageConsumer.cs",
                20,
                28),
            [
                new Context360RelatedNode(2, "HttpEndpoint.Handle"),
                new Context360RelatedNode(12, "PaymentHook.Execute")
            ],
            [],
            [
                new Context360RelatedNode(3, "MessageProvider.GetMessage")
            ],
            []);

        var output = TokenObjectNotation.SerializeContext360(result);

        output.Should().Be(
            "node:" + Environment.NewLine +
            "  id: 1" + Environment.NewLine +
            "  name: MessageConsumer.Render()" + Environment.NewLine +
            "  kind: M" + Environment.NewLine +
            "  file: src/Fixture.App/MessageConsumer.cs:20-28" + Environment.NewLine +
            Environment.NewLine +
            "incoming:" + Environment.NewLine +
            "  callers: [HttpEndpoint.Handle (Id:2), PaymentHook.Execute (Id:12)]" + Environment.NewLine +
            "  implementers: []" + Environment.NewLine +
            Environment.NewLine +
            "outgoing:" + Environment.NewLine +
            "  callees: [MessageProvider.GetMessage (Id:3)]" + Environment.NewLine +
            "  inherits: []");
    }

    [Fact]
    public void WhenSerializeSemanticSearchHasMultipleDirectoriesAndFiles_ThenItFormatsHierarchicalToonOutput()
    {
        HybridSearchHit[] results =
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
                559,
                "node-dependency-graph-to-selected",
                "project-app",
                "Fixture.SelectedGraphNodeMapper.ToSelectedGraphNode(GraphNode)",
                "ToSelectedGraphNode(GraphNode)",
                NodeType.Method,
                "src/SharpSense.Infrastructure/DependencyGraph/SelectedGraphNodeMapper.cs",
                14,
                15,
                "Maps a selected graph node."),
            new HybridSearchHit(
                610,
                "node-knowledge-graph-build",
                "project-app",
                "Fixture.KnowledgeGraphRepository.BuildGraphNodeRecords(GraphNode, CancellationToken)",
                "BuildGraphNodeRecords(GraphNode, CancellationToken)",
                NodeType.Method,
                "src/SharpSense.Infrastructure/Indexing/KnowledgeGraphRepository.cs",
                422,
                456,
                "Builds graph records."),
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
        ];

        var output = TokenObjectNotation.SerializeSemanticSearch(results);

        output.Should().Be(
            "src/SharpSense.Infrastructure/DependencyGraph/:" + Environment.NewLine +
            "  DependencyGraphMapper.cs:" + Environment.NewLine +
            "    - [M] `553` ToExternalGraphNode L20-21" + Environment.NewLine +
            "    - [M] `556` ToGraphNode L32-47" + Environment.NewLine +
            "  SelectedGraphNodeMapper.cs:" + Environment.NewLine +
            "    - [M] `559` ToSelectedGraphNode L14-15" + Environment.NewLine +
            Environment.NewLine +
            "src/SharpSense.Infrastructure/Indexing/:" + Environment.NewLine +
            "  KnowledgeGraphRepository.cs:" + Environment.NewLine +
            "    - [M] `610` BuildGraphNodeRecords L422-456" + Environment.NewLine +
            Environment.NewLine +
            "src/SharpSense.Domain/KnowledgeGraph/Nodes/:" + Environment.NewLine +
            "  ProjectNode.cs:" + Environment.NewLine +
            "    - [P] `373` Id L5");
    }

    [Fact]
    public void WhenSerializeCalleeTraceHasRootAndChild_ThenItFormatsArrowChainOutput()
    {
        var output = TokenObjectNotation.SerializeCalleeTrace(
            new CodeNodeResult(
                1,
                "node-root",
                "project-app",
                "Fixture.App.MessageConsumer.Render()",
                "MessageConsumer.Render()",
                NodeType.Method,
                "src/Fixture.App/MessageConsumer.cs",
                20,
                28,
                "Renders a message."),
            [
                new CodeNodeResult(
                    7,
                    "node-message-provider",
                    "project-app",
                    "Fixture.App.MessageProvider.GetMessage()",
                    "MessageProvider.GetMessage()",
                    NodeType.Method,
                    "src/Fixture.App/MessageProvider.cs",
                    7,
                    11,
                    "Gets a message.")
            ]);

        output.Should().Be(
            "- [M] `1` MessageConsumer.Render @ src/Fixture.App/MessageConsumer.cs:L20-28" + Environment.NewLine +
            "  -> [M] `7` MessageProvider.GetMessage @ src/Fixture.App/MessageProvider.cs:L7-11");
    }

    [Fact]
    public void WhenSerializeCallerTraceHasDependencyChain_ThenItFormatsLeafToTargetOutput()
    {
        var rootNode = new CodeNodeResult(
            1,
            "node-root",
            "project-app",
            "Fixture.App.MessageConsumer.Render()",
            "MessageConsumer.Render()",
            NodeType.Method,
            "src/Fixture.App/MessageConsumer.cs",
            20,
            28,
            "Renders a message.");
        var output = TokenObjectNotation.SerializeCallerTrace(
            rootNode,
            [
                new CodeNodeResult(
                    2,
                    "node-caller",
                    "project-app",
                    "Fixture.App.HttpEndpoint.Handle()",
                    "HttpEndpoint.Handle()",
                    NodeType.Method,
                    "src/Fixture.App/HttpEndpoint.cs",
                    5,
                    12,
                    "Handles a request."),
                new CodeNodeResult(
                    3,
                    "node-upstream",
                    "project-app",
                    "Fixture.App.ApiGateway.Dispatch()",
                    "ApiGateway.Dispatch()",
                    NodeType.Method,
                    "src/Fixture.App/ApiGateway.cs",
                    2,
                    9,
                    "Dispatches the endpoint.")
            ],
            [
                new ImpactedDependencyEdge("node-caller", "node-root", EdgeType.MethodCall),
                new ImpactedDependencyEdge("node-upstream", "node-caller", EdgeType.MethodCall)
            ]);

        output.Should().Be(
            "- [M] `3` ApiGateway.Dispatch @ src/Fixture.App/ApiGateway.cs:L2-9" + Environment.NewLine +
            "  -> [M] `2` HttpEndpoint.Handle @ src/Fixture.App/HttpEndpoint.cs:L5-12" + Environment.NewLine +
            "    -> [M] `1` MessageConsumer.Render @ src/Fixture.App/MessageConsumer.cs:L20-28");
    }

    [Fact]
    public void WhenSerializeRefactorResultSucceeds_ThenItFormatsModifiedFiles()
    {
        var output = TokenObjectNotation.SerializeRefactorResult(new RefactorResult(
            true,
            ["src/Fixture.App/Feature.cs"],
            string.Empty));

        output.Should().Be(
            "refactor_success: true" + Environment.NewLine +
            "modified_files:" + Environment.NewLine +
            "  - src/Fixture.App/Feature.cs");
    }

    [Fact]
    public void WhenSerializeRefactorResultFails_ThenItFormatsTheErrorMessage()
    {
        var output = TokenObjectNotation.SerializeRefactorResult(new RefactorResult(
            false,
            [],
            "Unable to locate document 'src/Fixture.App/Feature.cs'."));

        output.Should().Be(
            "refactor_success: false" + Environment.NewLine +
            "error_message: Unable to locate document 'src/Fixture.App/Feature.cs'.");
    }

    [Fact]
    public void WhenSerializeCommandExecutionResultHasBlocks_ThenItFormatsMetadataAndRanges()
    {
        var output = TokenObjectNotation.SerializeCommandExecutionResult(new CommandExecutionResult(
            "dotnet build SharpSense.sln",
            "/repo",
            "Build succeeded",
            0,
            1201,
            1,
            false,
            "Returned 1 merged block(s) from 1 matched line(s) across 1201 captured line(s).",
            [
                new CommandExecutionBlock(
                    1201,
                    1201,
                    "1201| Build succeeded in 13.7s")
            ]));

        output.Should().Be(
            "command: dotnet build SharpSense.sln" + Environment.NewLine +
            "status: success" + Environment.NewLine +
            "exit_code: 0" + Environment.NewLine +
            "working_directory: /repo" + Environment.NewLine +
            "query: Build succeeded" + Environment.NewLine +
            "metrics:" + Environment.NewLine +
            "  captured_lines: 1201" + Environment.NewLine +
            "  matched_lines: 1" + Environment.NewLine +
            "  block_count: 1" + Environment.NewLine +
            "  truncated: false" + Environment.NewLine +
            "summary: Returned 1 merged block(s) from 1 matched line(s) across 1201 captured line(s)." + Environment.NewLine +
            "output:" + Environment.NewLine +
            "  - span: 1201-1201" + Environment.NewLine +
            "    text: |" + Environment.NewLine +
            "      1201| Build succeeded in 13.7s");
    }

    [Fact]
    public void WhenSerializeCommandExecutionFailureHasMessage_ThenItFormatsErrorOutput()
    {
        var output = TokenObjectNotation.SerializeCommandExecutionFailure(
            "missing-command",
            "Failed to start command 'missing-command'.");

        output.Should().Be(
            "command: missing-command" + Environment.NewLine +
            "status: error" + Environment.NewLine +
            "error_message: Failed to start command 'missing-command'.");
    }
}
