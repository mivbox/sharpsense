using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed partial class EdgeExtractor
{
    private static void AddServiceRegistrationEdges(
        string callerId,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? methodSymbol,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        var registeredTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var registeredType in GetServiceRegistrationTargetTypes(semanticModel, invocation, methodSymbol))
        {
            if (!registeredTypes.Add(registeredType))
            {
                continue;
            }

            TryAddEdge(callerId, registeredType, EdgeType.ServiceRegistration, symbolNodeIds, edgeKeys);
        }
    }

    private static void AddServiceLocatorEdges(
        string callerId,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? methodSymbol,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        var resolvedTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var resolvedType in GetInvocationTargetTypes(semanticModel, invocation, methodSymbol))
        {
            if (!resolvedTypes.Add(resolvedType))
            {
                continue;
            }

            TryAddEdge(
                callerId,
                resolvedType,
                GetDependencyResolutionEdgeType(resolvedType),
                symbolNodeIds,
                edgeKeys);
        }
    }

    private static ServiceReceiverKind GetServiceReceiverKind(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation)
    {
        if (!TryGetInvocationReceiverExpression(invocation, out var receiverExpression))
        {
            return ServiceReceiverKind.None;
        }

        if (IsServiceCollectionReceiver(semanticModel, receiverExpression))
        {
            return ServiceReceiverKind.ServiceCollection;
        }

        return IsServiceProviderReceiver(semanticModel, receiverExpression)
            ? ServiceReceiverKind.ServiceProvider
            : ServiceReceiverKind.None;
    }

    private static bool IsStaticPartialFactoryMethod(IMethodSymbol methodSymbol)
        => methodSymbol.IsStatic
           && !methodSymbol.ReturnsVoid
           && (methodSymbol.IsPartialDefinition || methodSymbol.PartialDefinitionPart is not null);

    private static bool TryGetInvocationReceiverExpression(
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax receiverExpression)
    {
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax memberAccessExpression:
                receiverExpression = memberAccessExpression.Expression;
                return true;
            case MemberBindingExpressionSyntax
                when invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccessExpression:
                receiverExpression = conditionalAccessExpression.Expression;
                return true;
            default:
                receiverExpression = null!;
                return false;
        }
    }

    private static bool IsServiceCollectionReceiver(
        SemanticModel semanticModel,
        ExpressionSyntax receiverExpression)
    {
        return IsKnownReceiver(semanticModel, receiverExpression, ServiceReceiverKind.ServiceCollection);
    }

    private static bool IsServiceProviderReceiver(
        SemanticModel semanticModel,
        ExpressionSyntax receiverExpression)
    {
        return IsKnownReceiver(semanticModel, receiverExpression, ServiceReceiverKind.ServiceProvider);
    }

    private static bool IsKnownReceiver(
        SemanticModel semanticModel,
        ExpressionSyntax receiverExpression,
        ServiceReceiverKind receiverKind)
    {
        var receiverType = semanticModel.GetTypeInfo(receiverExpression);
        if (IsKnownReceiverType(receiverType.Type, receiverKind) ||
            IsKnownReceiverType(receiverType.ConvertedType, receiverKind))
        {
            return true;
        }

        var receiverSymbol = RoslynSymbolUtilities.ResolveReferencedSymbol(
            semanticModel.GetSymbolInfo(receiverExpression));

        return IsKnownReceiverType(GetTypeSymbol(receiverSymbol), receiverKind);
    }

    private static ITypeSymbol? GetTypeSymbol(ISymbol? symbol)
    {
        return symbol switch
        {
            IFieldSymbol fieldSymbol => fieldSymbol.Type,
            ILocalSymbol localSymbol => localSymbol.Type,
            IParameterSymbol parameterSymbol => parameterSymbol.Type,
            IPropertySymbol propertySymbol => propertySymbol.Type,
            _ => null
        };
    }

    private static bool IsKnownReceiverType(ITypeSymbol? typeSymbol, ServiceReceiverKind receiverKind)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (IsKnownReceiverInterface(typeSymbol, receiverKind))
        {
            return true;
        }

        foreach (var interfaceSymbol in typeSymbol.AllInterfaces)
        {
            if (IsKnownReceiverInterface(interfaceSymbol, receiverKind))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownReceiverInterface(
        ITypeSymbol typeSymbol,
        ServiceReceiverKind receiverKind)
    {
        return receiverKind switch
        {
            ServiceReceiverKind.ServiceCollection => IsKnownInterface(
                typeSymbol,
                _serviceCollectionInterfaceName,
                _serviceCollectionNamespace),
            ServiceReceiverKind.ServiceProvider => IsKnownInterface(
                typeSymbol,
                _serviceProviderInterfaceName,
                _serviceProviderNamespace),
            _ => false
        };
    }

    private static bool IsKnownInterface(
        ITypeSymbol typeSymbol,
        string interfaceName,
        string interfaceNamespace)
    {
        return typeSymbol is INamedTypeSymbol namedTypeSymbol &&
               string.Equals(namedTypeSymbol.Name, interfaceName, StringComparison.Ordinal) &&
               (string.Equals(
                    namedTypeSymbol.ContainingNamespace.ToDisplayString(),
                    interfaceNamespace,
                    StringComparison.Ordinal) ||
                (namedTypeSymbol.TypeKind == TypeKind.Error &&
                 namedTypeSymbol.ContainingNamespace.IsGlobalNamespace));
    }

    private static ITypeSymbol? GetFactoryReturnType(ITypeSymbol returnType)
    {
        return returnType switch
        {
            IArrayTypeSymbol arrayType => arrayType.ElementType,
            { SpecialType: SpecialType.System_Void } => null,
            _ => returnType
        };
    }

    private static IEnumerable<ITypeSymbol> GetInvocationTargetTypes(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? methodSymbol)
    {
        if (TryGetGenericInvocationName(invocation.Expression, out var genericName))
        {
            foreach (var typeArgument in genericName.TypeArgumentList.Arguments)
            {
                if (semanticModel.GetTypeInfo(typeArgument).Type is { TypeKind: not TypeKind.TypeParameter } targetType)
                {
                    yield return targetType;
                }
            }
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is not TypeOfExpressionSyntax typeOfExpression)
            {
                continue;
            }

            if (semanticModel.GetTypeInfo(typeOfExpression.Type).Type is { TypeKind: not TypeKind.TypeParameter } targetType)
            {
                yield return targetType;
            }
        }

        if (methodSymbol is null)
        {
            yield break;
        }

        foreach (var typeArgument in methodSymbol.TypeArguments)
        {
            if (typeArgument.TypeKind is TypeKind.TypeParameter)
            {
                continue;
            }

            yield return typeArgument;
        }
    }

    private static IEnumerable<ITypeSymbol> GetServiceRegistrationTargetTypes(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? methodSymbol)
    {
        var invocationTargetTypes = GetInvocationTargetTypes(semanticModel, invocation, methodSymbol).ToArray();
        if (invocationTargetTypes.Length > 0)
        {
            foreach (var targetType in invocationTargetTypes)
            {
                yield return targetType;
            }

            yield break;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is TypeOfExpressionSyntax)
            {
                continue;
            }

            if (semanticModel.GetTypeInfo(argument.Expression).Type is { TypeKind: not TypeKind.TypeParameter } targetType)
            {
                yield return targetType;
            }
        }
    }

    private static bool TryGetGenericInvocationName(
        ExpressionSyntax invocationExpression,
        out GenericNameSyntax genericName)
    {
        switch (invocationExpression)
        {
            case MemberAccessExpressionSyntax { Name: GenericNameSyntax memberGenericName }:
                genericName = memberGenericName;
                return true;
            case MemberBindingExpressionSyntax { Name: GenericNameSyntax memberGenericName }:
                genericName = memberGenericName;
                return true;
            case GenericNameSyntax directGenericName:
                genericName = directGenericName;
                return true;
            default:
                genericName = null!;
                return false;
        }
    }

    private static EdgeType GetDependencyResolutionEdgeType(ITypeSymbol typeSymbol)
    {
        return typeSymbol switch
        {
            { TypeKind: TypeKind.Interface } => EdgeType.MethodCall,
            INamedTypeSymbol { IsAbstract: true } => EdgeType.MethodCall,
            _ => EdgeType.Instantiates
        };
    }

    private enum ServiceReceiverKind
    {
        None,
        ServiceCollection,
        ServiceProvider
    }
}
