using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.ImpactAnalysis;

internal static class ImpactAnalysisMapper
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
}
