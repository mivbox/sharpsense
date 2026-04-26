using Microsoft.CodeAnalysis;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed partial class EdgeExtractor
{
    private static void TryAddEdge(
        string callerId,
        ISymbol? targetSymbol,
        EdgeType edgeType,
        SymbolNodeResolver nodeResolver,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        if (targetSymbol is null)
        {
            return;
        }

        if (!nodeResolver.TryGetNodeId(targetSymbol, out var calleeId))
        {
            return;
        }

        if (string.Equals(callerId, calleeId, StringComparison.Ordinal))
        {
            return;
        }

        edgeKeys.Add((callerId, calleeId, edgeType));
    }
}
