using Microsoft.CodeAnalysis;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed partial class EdgeExtractor
{
    private static void AddStructuralHierarchyEdges(
        IReadOnlyList<DeclaredSymbolContext> declaredSymbols,
        SymbolNodeResolver nodeResolver,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        foreach (var declaredSymbol in declaredSymbols)
        {
            switch (declaredSymbol.Symbol)
            {
                case INamedTypeSymbol namedTypeSymbol:
                    AddTypeParentEdge(declaredSymbol, namedTypeSymbol, nodeResolver, edgeKeys);
                    break;
                case IMethodSymbol
                { MethodKind: MethodKind.Ordinary }:
                case IPropertySymbol:
                case IFieldSymbol:
                    AddMemberParentEdge(declaredSymbol.NodeId, declaredSymbol.Symbol, nodeResolver, edgeKeys);
                    break;
            }
        }
    }

    private static void AddTypeParentEdge(
        DeclaredSymbolContext declaredSymbol,
        INamedTypeSymbol typeSymbol,
        SymbolNodeResolver nodeResolver,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        if (typeSymbol.ContainingType is not null &&
            nodeResolver.TryGetNodeId(typeSymbol.ContainingType, out var containingTypeNodeId))
        {
            TryAddEdge(containingTypeNodeId, declaredSymbol.NodeId, EdgeType.ParentOf, edgeKeys);

            return;
        }

        TryAddEdge(declaredSymbol.ProjectId, declaredSymbol.NodeId, EdgeType.ParentOf, edgeKeys);
    }

    private static void AddMemberParentEdge(
        string memberNodeId,
        ISymbol memberSymbol,
        SymbolNodeResolver nodeResolver,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        if (memberSymbol.ContainingType is null ||
            !nodeResolver.TryGetNodeId(memberSymbol.ContainingType, out var parentTypeNodeId))
        {
            return;
        }

        TryAddEdge(parentTypeNodeId, memberNodeId, EdgeType.ParentOf, edgeKeys);
    }
}
