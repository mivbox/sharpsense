using Microsoft.CodeAnalysis;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed partial class EdgeExtractor
{
    private static void TryAddEdge(
        string callerId,
        ISymbol? targetSymbol,
        EdgeType edgeType,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        if (targetSymbol is null)
        {
            return;
        }

        var canonicalTargetSymbol = RoslynSymbolUtilities.Canonicalize(targetSymbol);
        if (!TryGetNodeId(symbolNodeIds, targetSymbol, canonicalTargetSymbol, out var calleeId))
        {
            return;
        }

        if (string.Equals(callerId, calleeId, StringComparison.Ordinal))
        {
            return;
        }

        edgeKeys.Add((callerId, calleeId, edgeType));
    }

    private static bool TryGetNodeId(
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISymbol symbol,
        ISymbol canonicalSymbol,
        out string calleeId)
    {
        var canonicalLookupKey = RoslynSymbolUtilities.GetLookupKey(canonicalSymbol);
        if (symbolNodeIds.TryGetValue(canonicalLookupKey, out var canonicalNodeId))
        {
            calleeId = canonicalNodeId;
            return true;
        }

        var symbolLookupKey = RoslynSymbolUtilities.GetLookupKey(symbol);
        if (symbolNodeIds.TryGetValue(symbolLookupKey, out var symbolNodeId))
        {
            calleeId = symbolNodeId;
            return true;
        }

        calleeId = string.Empty;
        return false;
    }
}
