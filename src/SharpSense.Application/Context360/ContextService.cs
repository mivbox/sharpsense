using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Context360;

public sealed class ContextService(IContextLookup contextLookup)
    : IContextService
{
    public async Task<Context360Result> GetNodeContext(
        int nodeId,
        int maxRelated,
        CancellationToken ct)
    {
        if (nodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId), nodeId, "NodeId must be greater than zero.");
        }

        var normalizedMaxRelated = Math.Clamp(maxRelated, 1, 50);
        var context = await contextLookup.GetNodeContext(
            nodeId,
            normalizedMaxRelated,
            ct);

        if (context is null)
        {
            throw new InvalidOperationException($"No persisted node exists for id {nodeId}.");
        }

        return new Context360Result(
            MapTargetNode(context.TargetNode),
            MapRelatedNodes(context.Callers),
            MapRelatedNodes(context.Implementers),
            MapRelatedNodes(context.Callees),
            MapRelatedNodes(context.Inherits));
    }

    private static Context360Node MapTargetNode(CodeNodeResult node)
        => new(
            node.Id,
            node.DisplayName,
            node.NodeType,
            node.RelativeFilePath,
            node.StartLine,
            node.EndLine);

    private static Context360RelatedNode[] MapRelatedNodes(IEnumerable<CodeNodeResult> nodes)
        => [.. nodes.Select(static node => new Context360RelatedNode(
            node.Id,
            SanitizeRelatedNodeName(node.NodeType, node.DisplayName)))];

    private static string SanitizeRelatedNodeName(
        NodeType nodeType,
        string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (nodeType is not NodeType.Method)
        {
            return displayName;
        }

        var parameterListStart = displayName.IndexOf('(');
        return parameterListStart < 0
            ? displayName
            : displayName[..parameterListStart].TrimEnd();
    }
}
