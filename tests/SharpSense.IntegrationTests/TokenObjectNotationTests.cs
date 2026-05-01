using AwesomeAssertions;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Cli.Shared;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.IntegrationTests;

public sealed class TokenObjectNotationTests
{
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
