using SharpSense.Application.Features.ImpactAnalysis.Contracts;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.ImpactAnalysis;

public static class ImpactAnalysisMapper
{
    public static ImpactedCodeNode ToImpactedCodeNode(CodeNode codeNode)
    {
        ArgumentNullException.ThrowIfNull(codeNode);

        return new ImpactedCodeNode(
            codeNode.Id,
            codeNode.CanonicalId,
            codeNode.ProjectId,
            codeNode.FullyQualifiedName,
            codeNode.DisplayName,
            codeNode.NodeType,
            codeNode.RelativeFilePath,
            codeNode.StartLine,
            codeNode.EndLine,
            codeNode.Summary);
    }

    public static ImpactedCodeNode[] ToImpactedCodeNodes(IEnumerable<CodeNode> codeNodes)
        => [.. codeNodes.Select(ToImpactedCodeNode)];

    public static ImpactedDependencyEdge ToImpactedDependencyEdge(DependencyEdge dependencyEdge)
    {
        ArgumentNullException.ThrowIfNull(dependencyEdge);

        return new ImpactedDependencyEdge(
            dependencyEdge.CallerId,
            dependencyEdge.CalleeId,
            dependencyEdge.EdgeType);
    }

    public static ImpactedDependencyEdge[] ToImpactedDependencyEdges(IEnumerable<DependencyEdge> dependencyEdges)
        => [.. dependencyEdges.Select(ToImpactedDependencyEdge)];
}
