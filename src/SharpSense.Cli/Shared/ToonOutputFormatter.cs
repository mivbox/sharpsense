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
                node.RelativeFilePath,
                node.StartLine,
                node.EndLine)));
    }

    internal static string FormatNode(
        NodeType nodeType,
        string nodeId,
        string relativeFilePath,
        int startLine,
        int endLine)
    {
        // Added backticks around {nodeId} to create a bulletproof parsing boundary for LLMs
        return $"[{GetNodeTypeShorthand(nodeType)}] `{nodeId}` @ {relativeFilePath}:{startLine}-{endLine}";
    }

    private static string GetNodeTypeShorthand(NodeType nodeType) =>
        nodeType switch
        {
            NodeType.Method => "M",
            NodeType.Class => "C",
            NodeType.Interface => "I",
            NodeType.Property => "P",
            NodeType.Field => "F",
            _ => throw new InvalidOperationException($"Unsupported node type '{nodeType}'.")
        };
}
