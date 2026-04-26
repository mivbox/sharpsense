using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed partial class EdgeExtractor
{
    private static void AddMemberDependencyEdges(
        IReadOnlyList<DeclaredSymbolContext> declaredSymbols,
        SymbolNodeResolver nodeResolver,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        foreach (var declaredSymbol in declaredSymbols)
        {
            AddMemberDependencyEdges(declaredSymbol, nodeResolver, edgeKeys);
        }
    }

    private static void AddMemberDependencyEdges(
        DeclaredSymbolContext declaredSymbol,
        SymbolNodeResolver nodeResolver,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        if (declaredSymbol.Symbol is not (IMethodSymbol or IPropertySymbol or IFieldSymbol))
        {
            return;
        }

        if (declaredSymbol.Symbol is IMethodSymbol methodSymbol)
        {
            AddFactoryInstantiationEdges(declaredSymbol.NodeId, methodSymbol, nodeResolver, edgeKeys);
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
                        nodeResolver,
                        edgeKeys);
                    break;
                case ObjectCreationExpressionSyntax objectCreation:
                    ProcessObjectCreation(
                        declaredSymbol.NodeId,
                        declaredSymbol.SemanticModel,
                        objectCreation,
                        nodeResolver,
                        edgeKeys);
                    break;
                case ImplicitObjectCreationExpressionSyntax implicitCreation:
                    ProcessImplicitCreation(
                        declaredSymbol.NodeId,
                        declaredSymbol.SemanticModel,
                        implicitCreation,
                        nodeResolver,
                        edgeKeys);
                    break;
                case IdentifierNameSyntax identifier:
                    ProcessIdentifier(
                        declaredSymbol.NodeId,
                        declaredSymbol.SemanticModel,
                        identifier,
                        nodeResolver,
                        edgeKeys);
                    break;
            }
        }
    }

    private static void ProcessInvocation(
        string callerId,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        SymbolNodeResolver nodeResolver,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        var referencedSymbol = RoslynSymbolUtilities.ResolveReferencedSymbol(
            semanticModel.GetSymbolInfo(invocation));
        TryAddEdge(callerId, referencedSymbol, EdgeType.MethodCall, nodeResolver, edgeKeys);
        var referencedMethodSymbol = referencedSymbol as IMethodSymbol;

        switch (GetServiceReceiverKind(semanticModel, invocation))
        {
            case ServiceReceiverKind.ServiceCollection:
                AddServiceRegistrationEdges(callerId, semanticModel, invocation, referencedMethodSymbol, nodeResolver, edgeKeys);
                break;
            case ServiceReceiverKind.ServiceProvider:
                AddServiceLocatorEdges(callerId, semanticModel, invocation, referencedMethodSymbol, nodeResolver, edgeKeys);
                break;
        }
    }

    private static void ProcessObjectCreation(
        string callerId,
        SemanticModel semanticModel,
        ObjectCreationExpressionSyntax objectCreation,
        SymbolNodeResolver nodeResolver,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        TryAddEdge(
            callerId,
            semanticModel.GetTypeInfo(objectCreation).Type,
            EdgeType.Instantiates,
            nodeResolver,
            edgeKeys);
    }

    private static void ProcessImplicitCreation(
        string callerId,
        SemanticModel semanticModel,
        ImplicitObjectCreationExpressionSyntax implicitCreation,
        SymbolNodeResolver nodeResolver,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        TryAddEdge(
            callerId,
            semanticModel.GetTypeInfo(implicitCreation).Type,
            EdgeType.Instantiates,
            nodeResolver,
            edgeKeys);
    }

    private static void ProcessIdentifier(
        string callerId,
        SemanticModel semanticModel,
        IdentifierNameSyntax identifier,
        SymbolNodeResolver nodeResolver,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        var referencedSymbol = RoslynSymbolUtilities.ResolveReferencedSymbol(
            semanticModel.GetSymbolInfo(identifier));
        if (referencedSymbol is not (IFieldSymbol or IPropertySymbol))
        {
            return;
        }

        TryAddEdge(callerId, referencedSymbol, EdgeType.FieldAccess, nodeResolver, edgeKeys);
    }

    private static void AddFactoryInstantiationEdges(
        string callerId,
        IMethodSymbol methodSymbol,
        SymbolNodeResolver nodeResolver,
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

        TryAddEdge(callerId, instantiatedType, EdgeType.Instantiates, nodeResolver, edgeKeys);
    }
}
