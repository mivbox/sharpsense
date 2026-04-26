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

        return $"[{GetNodeTypeShorthand(nodeType)}] `{nodeId}` {displayName} @ {relativeFilePath}:{startLine}-{endLine}";
    }

    private static string GetNodeTypeShorthand(NodeType nodeType) =>
        nodeType switch
        {
            NodeType.Method => "M",
            NodeType.Class => "C",
            NodeType.Interface => "I",
            NodeType.Property => "P",
            NodeType.Field => "F",
            NodeType.Document => "D",
            _ => throw new InvalidOperationException($"Unsupported node type '{nodeType}'.")
        };
}
