using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.DependencyGraph;

internal static class DependencyGraphMapper
{
    private const string SelectedScope = "selected";
    private const string ExternalScope = "external";
    private const string InternalEdgeScope = "internal";
    private const string BoundaryEdgeScope = "boundary";

    public static GraphNode ToSelectedGraphNode(ProjectNode projectNode)
        => ToGraphNode(projectNode, SelectedScope, true);

    public static GraphNode ToSelectedGraphNode(CodeNode codeNode)
        => ToGraphNode(codeNode, SelectedScope, true);

    public static GraphNode ToExternalGraphNode(ProjectNode projectNode)
        => ToGraphNode(projectNode, ExternalScope, false);

    public static GraphNode ToExternalGraphNode(CodeNode codeNode)
        => ToGraphNode(codeNode, ExternalScope, false);

    public static GraphEdge ToInternalGraphEdge(DependencyEdge dependencyEdge)
        => ToGraphEdge(dependencyEdge, InternalEdgeScope);

    public static GraphEdge ToBoundaryGraphEdge(DependencyEdge dependencyEdge)
        => ToGraphEdge(dependencyEdge, BoundaryEdgeScope);

    private static GraphNode ToGraphNode(
        ProjectNode projectNode,
        string scope,
        bool isClickable)
    {
        ArgumentNullException.ThrowIfNull(projectNode);

        return new GraphNode(
            projectNode.Id,
            projectNode.Name,
            "project",
            projectNode.RelativeFilePath,
            projectNode.Id,
            scope,
            isClickable);
    }

    private static GraphNode ToGraphNode(
        CodeNode codeNode,
        string scope,
        bool isClickable)
    {
        ArgumentNullException.ThrowIfNull(codeNode);

        return new GraphNode(
            codeNode.CanonicalId,
            codeNode.FullyQualifiedName,
            codeNode.NodeType.ToString().ToLowerInvariant(),
            codeNode.RelativeFilePath,
            codeNode.ProjectId,
            scope,
            isClickable);
    }

    private static GraphEdge ToGraphEdge(
        DependencyEdge dependencyEdge,
        string scope)
    {
        ArgumentNullException.ThrowIfNull(dependencyEdge);

        var type = dependencyEdge.EdgeType.ToString().ToLowerInvariant();
        return new GraphEdge(
            $"{dependencyEdge.CallerId}|{dependencyEdge.CalleeId}|{type}",
            dependencyEdge.CallerId,
            dependencyEdge.CalleeId,
            type,
            scope);
    }
}
