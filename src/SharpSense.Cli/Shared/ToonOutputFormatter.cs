using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Cli.Shared;

internal sealed class ToonOutputFormatter : IOutputFormatter
{
    public string Format(IEnumerable<CodeNodeResult> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        return string.Join(
            Environment.NewLine,
            nodes.Select(static node => FormatNode(
                node.NodeType,
                node.Id,
                node.DisplayName,
                node.RelativeFilePath,
                node.StartLine,
                node.EndLine)));
    }

    internal static string FormatNode(
        NodeType nodeType,
        int nodeId,
        string displayName,
        string relativeFilePath,
        int startLine,
        int endLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFilePath);

        return $"[{TokenObjectNotation.GetNodeTypeShorthand(nodeType)}] `{nodeId}` {displayName} @ {relativeFilePath}:{startLine}-{endLine}";
    }
}
