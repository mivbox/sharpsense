using Riok.Mapperly.Abstractions;
using SharpSense.Application.Features.DependencyGraph.Contracts;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.DependencyGraph;

[Mapper]
internal static partial class DependencyGraphMapper
{
    /// <summary>
    /// Convert all nodes and edges into a single array.
    /// </summary>
    /// <param name="projectNodes"></param>
    /// <param name="codeNodes"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static GraphNode[] ToGraphNodes(
        ProjectNode[] projectNodes,
        CodeNode[] codeNodes)
    {
        ArgumentNullException.ThrowIfNull(projectNodes);
        ArgumentNullException.ThrowIfNull(codeNodes);

        return
        [
            ..projectNodes.Select(ToGraphNodes),
            ..codeNodes.Select(ToGraphNodes)
        ];
    }

    public static GraphEdge[] ToGraphEdges(DependencyEdge[] dependencyEdges)
    {
        ArgumentNullException.ThrowIfNull(dependencyEdges);
        return dependencyEdges.Select(ToGraphEdges).ToArray();
    }

    private static GraphNode ToGraphNodes(ProjectNode projectNode)
    {
        ArgumentNullException.ThrowIfNull(projectNode);
        return new GraphNode(projectNode.Id, projectNode.Name, "project");
    }

    private static GraphNode ToGraphNodes(CodeNode codeNode)
    {
        ArgumentNullException.ThrowIfNull(codeNode);
        return new GraphNode(codeNode.Id, codeNode.FullyQualifiedName, codeNode.NodeType.ToString().ToLowerInvariant());
    }

    private static GraphEdge ToGraphEdges(DependencyEdge dependencyEdge)
    {
        ArgumentNullException.ThrowIfNull(dependencyEdge);

        var type = dependencyEdge.EdgeType.ToString().ToLowerInvariant();
        return new GraphEdge(
            $"{dependencyEdge.CallerId}|{dependencyEdge.CalleeId}|{type}",
            dependencyEdge.CallerId,
            dependencyEdge.CalleeId,
            type);
    }
}
