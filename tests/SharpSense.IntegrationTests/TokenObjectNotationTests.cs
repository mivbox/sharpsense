using SharpSense.Application.Context360.Models;
using AwesomeAssertions;
using SharpSense.Application.HybridSearch.Models;
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
}
