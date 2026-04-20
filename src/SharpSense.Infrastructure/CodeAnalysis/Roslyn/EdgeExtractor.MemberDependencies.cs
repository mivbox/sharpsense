using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed partial class EdgeExtractor
{
    private static void AddMemberDependencyEdges(
        IReadOnlyList<DeclaredSymbolContext> declaredSymbols,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        foreach (var declaredSymbol in declaredSymbols)
        {
            AddMemberDependencyEdges(declaredSymbol, symbolNodeIds, edgeKeys);
        }
    }

    private static void AddMemberDependencyEdges(
        DeclaredSymbolContext declaredSymbol,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        if (declaredSymbol.Symbol is not (IMethodSymbol or IPropertySymbol or IFieldSymbol))
        {
            return;
        }

        if (declaredSymbol.Symbol is IMethodSymbol methodSymbol)
        {
            AddFactoryInstantiationEdges(declaredSymbol.NodeId, methodSymbol, symbolNodeIds, edgeKeys);
        }

        foreach (var node in declaredSymbol.DeclarationSyntax.DescendantNodes())
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    ProcessInvocation(
                        declaredSymbol.NodeId,
                        declaredSymbol.SemanticModel,
                        invocation,
                        symbolNodeIds,
                        edgeKeys);
                    break;
                case ObjectCreationExpressionSyntax objectCreation:
                    ProcessObjectCreation(
                        declaredSymbol.NodeId,
                        declaredSymbol.SemanticModel,
                        objectCreation,
                        symbolNodeIds,
                        edgeKeys);
                    break;
                case ImplicitObjectCreationExpressionSyntax implicitCreation:
                    ProcessImplicitCreation(
                        declaredSymbol.NodeId,
                        declaredSymbol.SemanticModel,
                        implicitCreation,
                        symbolNodeIds,
                        edgeKeys);
                    break;
                case IdentifierNameSyntax identifier:
                    ProcessIdentifier(
                        declaredSymbol.NodeId,
                        declaredSymbol.SemanticModel,
                        identifier,
                        symbolNodeIds,
                        edgeKeys);
                    break;
            }
        }
    }

    private static void ProcessInvocation(
        string callerId,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        var referencedSymbol = RoslynSymbolUtilities.ResolveReferencedSymbol(
            semanticModel.GetSymbolInfo(invocation));
        TryAddEdge(callerId, referencedSymbol, EdgeType.MethodCall, symbolNodeIds, edgeKeys);
        var referencedMethodSymbol = referencedSymbol as IMethodSymbol;

        switch (GetServiceReceiverKind(semanticModel, invocation))
        {
            case ServiceReceiverKind.ServiceCollection:
                AddServiceRegistrationEdges(callerId, semanticModel, invocation, referencedMethodSymbol, symbolNodeIds, edgeKeys);
                break;
            case ServiceReceiverKind.ServiceProvider:
                AddServiceLocatorEdges(callerId, semanticModel, invocation, referencedMethodSymbol, symbolNodeIds, edgeKeys);
                break;
        }
    }

    private static void ProcessObjectCreation(
        string callerId,
        SemanticModel semanticModel,
        ObjectCreationExpressionSyntax objectCreation,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        TryAddEdge(
            callerId,
            semanticModel.GetTypeInfo(objectCreation).Type,
            EdgeType.Instantiates,
            symbolNodeIds,
            edgeKeys);
    }

    private static void ProcessImplicitCreation(
        string callerId,
        SemanticModel semanticModel,
        ImplicitObjectCreationExpressionSyntax implicitCreation,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        TryAddEdge(
            callerId,
            semanticModel.GetTypeInfo(implicitCreation).Type,
            EdgeType.Instantiates,
            symbolNodeIds,
            edgeKeys);
    }

    private static void ProcessIdentifier(
        string callerId,
        SemanticModel semanticModel,
        IdentifierNameSyntax identifier,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        var referencedSymbol = RoslynSymbolUtilities.ResolveReferencedSymbol(
            semanticModel.GetSymbolInfo(identifier));
        if (referencedSymbol is not (IFieldSymbol or IPropertySymbol))
        {
            return;
        }

        TryAddEdge(callerId, referencedSymbol, EdgeType.FieldAccess, symbolNodeIds, edgeKeys);
    }

    private static void AddFactoryInstantiationEdges(
        string callerId,
        IMethodSymbol methodSymbol,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        if (!IsStaticPartialFactoryMethod(methodSymbol))
        {
            return;
        }

        var instantiatedType = GetFactoryReturnType(methodSymbol.ReturnType);
        if (instantiatedType is null)
        {
            return;
        }

        TryAddEdge(callerId, instantiatedType, EdgeType.Instantiates, symbolNodeIds, edgeKeys);
    }
}
