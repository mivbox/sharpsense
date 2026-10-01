using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Cli.Shared;

internal static class ImpactedNodeMapper
{
    public static CodeNodeResult[] Map(IEnumerable<ImpactedCodeNode> impactedNodes)
        =>
        [
            .. impactedNodes
                .Select(static node => new CodeNodeResult(
                    node.Id,
                    node.CanonicalId,
                    node.ProjectId,
                    node.FullyQualifiedName,
                    node.DisplayName,
                    node.NodeType,
                    node.RelativeFilePath,
                    node.StartLine,
                    node.EndLine,
                    node.Summary))
        ];
}
