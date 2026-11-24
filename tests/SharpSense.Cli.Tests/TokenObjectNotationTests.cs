using AwesomeAssertions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Memory.ListMemories.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Cli.Shared;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Cli.Tests;

public sealed class TokenObjectNotationTests
{
    [Fact]
    public void WhenSearchContainsTsxComponent_ThenFormatsComponentAlongsideCodeNodes()
    {
        HybridSearchHit[] results =
        [
            new(1, "component", null, "TotalView", "TotalView", NodeType.Component, "ui/TotalView.tsx", 2, 4, ""),
            new(2, "method", null, "total", "total", NodeType.Method, "ui/total.ts", 1, 1, "")
        ];

        var output = TokenObjectNotation.SerializeSemanticSearch(results);

        output.Should().Be(
            "ui/:" + Environment.NewLine +
            "  TotalView.tsx:" + Environment.NewLine +
            "    - [UI] `1` TotalView L2-4" + Environment.NewLine +
            "  total.ts:" + Environment.NewLine +
            "    - [M] `2` total L1");
    }

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
            [],
            [
                new Context360RelatedNode(20, "MessageConsumer")
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
            "  inherits: []" + Environment.NewLine +
            Environment.NewLine +
            "structural:" + Environment.NewLine +
            "  parents: [MessageConsumer (Id:20)]" + Environment.NewLine +
            "  children: []");
    }

    [Fact]
    public void WhenSerializeContext360HasSemanticContext_ThenItFormatsSeparateSemanticBlock()
    {
        var result = new Context360Result(
            new Context360Node(
                1,
                "MessageConsumer.Render()",
                NodeType.Method,
                "src/Fixture.App/MessageConsumer.cs",
                20,
                28),
            [],
            [],
            [],
            [],
            [],
            []);

        var output = TokenObjectNotation.SerializeContext360(
            result,
            [
                new MemoryNode(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "Fixture.App.MessageConsumer.Render()",
                    "hash-1",
                    "Security review note",
                    "content-hash",
                    ["security", "tech-debt"],
                    MemoryIntent.Invariant,
                    DateTimeOffset.Parse("2026-05-14T00:00:00+00:00"),
                    true)
            ]);

        output.Should().Be(
            "node:" + Environment.NewLine +
            "  id: 1" + Environment.NewLine +
            "  name: MessageConsumer.Render()" + Environment.NewLine +
            "  kind: M" + Environment.NewLine +
            "  file: src/Fixture.App/MessageConsumer.cs:20-28" + Environment.NewLine +
            Environment.NewLine +
            "incoming:" + Environment.NewLine +
            "  callers: []" + Environment.NewLine +
            "  implementers: []" + Environment.NewLine +
            Environment.NewLine +
            "outgoing:" + Environment.NewLine +
            "  callees: []" + Environment.NewLine +
            "  inherits: []" + Environment.NewLine +
            Environment.NewLine +
            "structural:" + Environment.NewLine +
            "  parents: []" + Environment.NewLine +
            "  children: []" + Environment.NewLine +
            "  memories: 1 (1 stale; use get_memory <id> to fetch content)" + Environment.NewLine +
            Environment.NewLine +
            "semantic_context:" + Environment.NewLine +
            "  memories:" + Environment.NewLine +
            "    - [*] id=11111111-1111-1111-1111-111111111111 intent=Invariant stale=true tags=[security, tech-debt] hint=\"verify memory against current source before replacing or deleting\"");
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
    public void WhenSerializeCalleeTraceHasSemanticMetadata_ThenItFormatsInlineMemoryLines()
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
            [],
            new Dictionary<int, MemoryNode[]>
            {
                [1] =
                [
                    new MemoryNode(
                        Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        "Fixture.App.MessageConsumer.Render()",
                        "hash-1",
                        "Security review note",
                        "content-hash",
                        ["security"],
                        MemoryIntent.Invariant,
                        DateTimeOffset.Parse("2026-05-14T00:00:00+00:00"),
                        false)
                ]
            });

        output.Should().Be(
            "- [M] `1` MessageConsumer.Render @ src/Fixture.App/MessageConsumer.cs:L20-28" + Environment.NewLine +
            "  semantic: [*] id=22222222-2222-2222-2222-222222222222 intent=Invariant stale=false tags=[security]" + Environment.NewLine +
            Environment.NewLine +
            "memories: 1 (0 stale; use get_memory <id> to fetch content)");
    }

    [Fact]
    public void WhenSerializingMemory_ThenFullContentIsRendered()
    {
        var memory = new MemoryNode(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Fixture.App.MessageConsumer.Render()",
            "hash-1",
            "Use \"debug\" mode for security review",
            "content-hash",
            ["security"],
            MemoryIntent.Invariant,
            DateTimeOffset.Parse("2026-05-14T00:00:00+00:00"),
            false);

        var output = TokenObjectNotation.SerializeMemory(memory);

        output.Should().Contain("id: 33333333-3333-3333-3333-333333333333");
        output.Should().Contain("target: Fixture.App.MessageConsumer.Render()");
        output.Should().Contain("stale: false");
        output.Should().Contain("tags: [security]");
        output.Should().Contain("Use \"debug\" mode for security review");
        output.Should().NotContain("hint:");
    }

    [Fact]
    public void WhenSerializingStaleMemory_ThenItIncludesVerificationHint()
    {
        var memory = new MemoryNode(
            Guid.NewGuid(),
            "Fixture.App.MessageConsumer.Render()",
            "hash-1",
            "Stale invariant",
            "content-hash",
            [],
            MemoryIntent.Invariant,
            DateTimeOffset.Parse("2026-05-14T00:00:00+00:00"),
            true);

        var output = TokenObjectNotation.SerializeMemory(memory);

        output.Should().Contain("stale: true");
        output.Should().Contain("hint: verify memory against current source before replacing or deleting");
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
                new CommandExecutionBlock(1201, 1201, "1201| Build succeeded in 13.7s")
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

    [Fact]
    public void WhenSerializeContext360HasTruncatedTotalsAndOmittedSections_ThenReportsCountsAndOmissions()
    {
        var result = new Context360Result(
            new Context360Node(
                1,
                "MessageConsumer.Render()",
                NodeType.Method,
                "src/Fixture.App/MessageConsumer.cs",
                20,
                28),
            [new Context360RelatedNode(2, "HttpEndpoint.Handle")],
            [],
            [],
            [],
            [],
            [],
            new Context360Totals(Callers: 5, Implementers: 0, Callees: 0, Inherits: 0, Parents: 0, Children: 0),
            [ContextSection.Callers, ContextSection.Implementers]);

        var output = TokenObjectNotation.SerializeContext360(result);

        output.Should().Be(
            "node:" + Environment.NewLine +
            "  id: 1" + Environment.NewLine +
            "  name: MessageConsumer.Render()" + Environment.NewLine +
            "  kind: M" + Environment.NewLine +
            "  file: src/Fixture.App/MessageConsumer.cs:20-28" + Environment.NewLine +
            Environment.NewLine +
            "incoming:" + Environment.NewLine +
            "  callers (1 of 5): [HttpEndpoint.Handle (Id:2)]" + Environment.NewLine +
            "  implementers: []" + Environment.NewLine +
            Environment.NewLine +
            "omitted_sections: [Callees, Inherits, Parents, Children]");
    }

    [Fact]
    public void WhenSerializeMemoryListHasItems_ThenFormatsCountsMetadataAndPreviews()
    {
        var memoryId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var list = new ListMemoriesResult(
            [
                new MemoryNodeSummary(
                    memoryId,
                    42,
                    "Fixture.App.ImpactAnalyzer.Analyze",
                    "src/ImpactAnalyzer.cs",
                    MemoryIntent.Invariant,
                    ["sqlite"],
                    true,
                    DateTimeOffset.Parse("2026-05-14T00:00:00+00:00"),
                    "Use a single deferred read transaction.")
            ],
            TotalCount: 3,
            StaleCount: 2,
            HasMore: true);

        var output = TokenObjectNotation.SerializeMemoryList(list);

        output.Should().Be(
            "memories: 1 of 3 (stale: 2, has_more: true)" + Environment.NewLine +
            "- [*] id=44444444-4444-4444-4444-444444444444 node=42 intent=Invariant stale=true tags=[sqlite] target=Fixture.App.ImpactAnalyzer.Analyze @ src/ImpactAnalyzer.cs" + Environment.NewLine +
            "  preview: Use a single deferred read transaction.");
    }

    [Fact]
    public void WhenSerializeCalleeTraceHasMultipleDepthsAndTruncation_ThenIndentsTreeAndMarksTruncated()
    {
        var root = new CodeNodeResult(1, "node-1", "p", "A.Root()", "A.Root()", NodeType.Method, "A.cs", 1, 5, "");
        var mid = new CodeNodeResult(2, "node-2", "p", "A.Mid()", "A.Mid()", NodeType.Method, "A.cs", 6, 10, "");
        var leaf = new CodeNodeResult(3, "node-3", "p", "A.Leaf()", "A.Leaf()", NodeType.Method, "A.cs", 11, 15, "");

        var output = TokenObjectNotation.SerializeCalleeTrace(
            root,
            [mid, leaf],
            dependencies:
            [
                new ImpactedDependencyEdge("node-1", "node-2", EdgeType.MethodCall),
                new ImpactedDependencyEdge("node-2", "node-3", EdgeType.MethodCall)
            ],
            truncated: true);

        output.Should().Be(
            "- [M] `1` A.Root @ A.cs:L1-5" + Environment.NewLine +
            "  -> [M] `2` A.Mid @ A.cs:L6-10" + Environment.NewLine +
            "    -> [M] `3` A.Leaf @ A.cs:L11-15" + Environment.NewLine +
            Environment.NewLine +
            "truncated: true (reached 1000-node limit; reduce maxDepth to narrow results)");
    }

    [Fact]
    public void WhenSerializeContext360HasOmittedSectionsAndSemanticContext_ThenMemorySummaryIsTopLevelAfterOmissions()
    {
        var result = new Context360Result(
            new Context360Node(
                1,
                "MessageConsumer.Render()",
                NodeType.Method,
                "src/Fixture.App/MessageConsumer.cs",
                20,
                28),
            [new Context360RelatedNode(2, "HttpEndpoint.Handle")],
            [],
            [],
            [],
            [],
            [],
            new Context360Totals(Callers: 1, Implementers: 0, Callees: 0, Inherits: 0, Parents: 0, Children: 0),
            [ContextSection.Callers]);

        var output = TokenObjectNotation.SerializeContext360(
            result,
            [
                new MemoryNode(
                    Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    "Fixture.App.MessageConsumer.Render()",
                    "hash-1",
                    "Keep deterministic ordering",
                    "content-hash",
                    ["ordering"],
                    MemoryIntent.Invariant,
                    DateTimeOffset.Parse("2026-05-14T00:00:00+00:00"),
                    false)
            ]);

        output.Should().Be(
            "node:" + Environment.NewLine +
            "  id: 1" + Environment.NewLine +
            "  name: MessageConsumer.Render()" + Environment.NewLine +
            "  kind: M" + Environment.NewLine +
            "  file: src/Fixture.App/MessageConsumer.cs:20-28" + Environment.NewLine +
            Environment.NewLine +
            "incoming:" + Environment.NewLine +
            "  callers: [HttpEndpoint.Handle (Id:2)]" + Environment.NewLine +
            Environment.NewLine +
            "omitted_sections: [Implementers, Callees, Inherits, Parents, Children]" + Environment.NewLine +
            Environment.NewLine +
            "memories: 1 (0 stale; use get_memory <id> to fetch content)" + Environment.NewLine +
            Environment.NewLine +
            "semantic_context:" + Environment.NewLine +
            "  memories:" + Environment.NewLine +
            "    - [*] id=55555555-5555-5555-5555-555555555555 intent=Invariant stale=false tags=[ordering]");
    }

    [Fact]
    public void WhenSerializeCalleeTraceHasSiblingCrossEdge_ThenRendersDirectCalleesAtShortestDepth()
    {
        var root = new CodeNodeResult(1, "node-root", "p", "A.Root()", "A.Root()", NodeType.Method, "A.cs", 1, 5, "");
        var first = new CodeNodeResult(2, "node-a", "p", "A.First()", "A.First()", NodeType.Method, "A.cs", 6, 10, "");
        var second = new CodeNodeResult(3, "node-b", "p", "A.Second()", "A.Second()", NodeType.Method, "A.cs", 11, 15, "");
        var leaf = new CodeNodeResult(4, "node-c", "p", "A.Leaf()", "A.Leaf()", NodeType.Method, "A.cs", 16, 20, "");

        var output = TokenObjectNotation.SerializeCalleeTrace(
            root,
            [first, second, leaf],
            dependencies:
            [
                new ImpactedDependencyEdge("node-root", "node-a", EdgeType.MethodCall),
                new ImpactedDependencyEdge("node-root", "node-b", EdgeType.MethodCall),
                new ImpactedDependencyEdge("node-a", "node-b", EdgeType.MethodCall),
                new ImpactedDependencyEdge("node-b", "node-c", EdgeType.MethodCall)
            ]);

        output.Should().Be(
            "- [M] `1` A.Root @ A.cs:L1-5" + Environment.NewLine +
            "  -> [M] `2` A.First @ A.cs:L6-10" + Environment.NewLine +
            "  -> [M] `3` A.Second @ A.cs:L11-15" + Environment.NewLine +
            "    -> [M] `4` A.Leaf @ A.cs:L16-20");
    }
}
