using Microsoft.CodeAnalysis;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed partial class EdgeExtractor
{
    private static void AddTypeDependencyEdges(
        IReadOnlyList<DeclaredSymbolContext> declaredSymbols,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        var processedTypeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var declaredSymbol in declaredSymbols)
        {
            if (declaredSymbol.Symbol is not INamedTypeSymbol namedTypeSymbol)
            {
                continue;
            }

            if (!processedTypeIds.Add(declaredSymbol.NodeId))
            {
                continue;
            }

            AddTypeDependencyEdges(declaredSymbol.NodeId, namedTypeSymbol, symbolNodeIds, edgeKeys);
        }
    }

    private static void AddTypeDependencyEdges(
        string callerId,
        INamedTypeSymbol typeSymbol,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        foreach (var interfaceSymbol in typeSymbol.Interfaces)
        {
            TryAddEdge(callerId, interfaceSymbol, EdgeType.Implements, symbolNodeIds, edgeKeys);
        }

        if (typeSymbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
        {
            TryAddEdge(callerId, baseType, EdgeType.Implements, symbolNodeIds, edgeKeys);
        }

        AddConstructorDependencyEdges(callerId, typeSymbol, symbolNodeIds, edgeKeys);
        AddImplementedMemberEdges(typeSymbol, symbolNodeIds, edgeKeys);
    }

    private static void AddConstructorDependencyEdges(
        string callerId,
        INamedTypeSymbol typeSymbol,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        var dependencyTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var constructor in typeSymbol.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0)
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.Type is not { TypeKind: not TypeKind.TypeParameter } dependencyType)
                {
                    continue;
                }

                if (!dependencyTypes.Add(dependencyType))
                {
                    continue;
                }

                TryAddEdge(
                    callerId,
                    dependencyType,
                    GetDependencyResolutionEdgeType(dependencyType),
                    symbolNodeIds,
                    edgeKeys);
            }
        }
    }

    private static void AddImplementedMemberEdges(
        INamedTypeSymbol typeSymbol,
        IReadOnlyDictionary<string, string> symbolNodeIds,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys)
    {
        foreach (var interfaceSymbol in typeSymbol.AllInterfaces)
        {
            foreach (var interfaceMember in interfaceSymbol.GetMembers())
            {
                if (interfaceMember is not IMethodSymbol { MethodKind: MethodKind.Ordinary }
                    && interfaceMember is not IPropertySymbol)
                {
                    continue;
                }

                foreach (var implementation in FindImplementationMembers(typeSymbol, interfaceMember))
                {
                    var canonicalImplementation = RoslynSymbolUtilities.Canonicalize(implementation);
                    if (!TryGetNodeId(symbolNodeIds, implementation, canonicalImplementation, out var implementationNodeId))
                    {
                        continue;
                    }

                    TryAddEdge(implementationNodeId, interfaceMember, EdgeType.Implements, symbolNodeIds, edgeKeys);
                }
            }
        }
    }

    private static IEnumerable<ISymbol> FindImplementationMembers(
        INamedTypeSymbol typeSymbol,
        ISymbol interfaceMember)
    {
        foreach (var candidateMember in EnumerateCandidateMembers(typeSymbol))
        {
            if (!ImplementsInterfaceMember(candidateMember, interfaceMember))
            {
                continue;
            }

            yield return candidateMember;
        }
    }

    private static IEnumerable<ISymbol> EnumerateCandidateMembers(INamedTypeSymbol typeSymbol)
    {
        for (var currentType = typeSymbol; currentType is not null; currentType = currentType.BaseType)
        {
            foreach (var member in currentType.GetMembers())
            {
                if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol)
                {
                    yield return member;
                }
            }
        }
    }

    private static bool ImplementsInterfaceMember(ISymbol candidateMember, ISymbol interfaceMember)
    {
        return (candidateMember, interfaceMember) switch
        {
            (IMethodSymbol candidateMethod, IMethodSymbol interfaceMethod) => MatchesMethod(candidateMethod, interfaceMethod),
            (IPropertySymbol candidateProperty, IPropertySymbol interfaceProperty) => MatchesProperty(candidateProperty, interfaceProperty),
            _ => false
        };
    }

    private static bool MatchesMethod(
        IMethodSymbol candidateMethod,
        IMethodSymbol interfaceMethod)
    {
        if (HasExplicitImplementation(candidateMethod.ExplicitInterfaceImplementations, interfaceMethod))
        {
            return true;
        }

        if (candidateMethod.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (!string.Equals(candidateMethod.Name, interfaceMethod.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (candidateMethod.IsStatic != interfaceMethod.IsStatic)
        {
            return false;
        }

        if (candidateMethod.TypeParameters.Length != interfaceMethod.TypeParameters.Length)
        {
            return false;
        }

        if (!HaveSameType(candidateMethod.ReturnType, interfaceMethod.ReturnType))
        {
            return false;
        }

        return HaveSameParameters(candidateMethod.Parameters, interfaceMethod.Parameters);
    }

    private static bool MatchesProperty(
        IPropertySymbol candidateProperty,
        IPropertySymbol interfaceProperty)
    {
        if (HasExplicitImplementation(candidateProperty.ExplicitInterfaceImplementations, interfaceProperty))
        {
            return true;
        }

        if (candidateProperty.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (!string.Equals(candidateProperty.Name, interfaceProperty.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (candidateProperty.IsIndexer != interfaceProperty.IsIndexer)
        {
            return false;
        }

        if (!HaveSameType(candidateProperty.Type, interfaceProperty.Type))
        {
            return false;
        }

        return HaveSameParameters(candidateProperty.Parameters, interfaceProperty.Parameters);
    }

    private static bool HasExplicitImplementation<TSymbol>(
        IEnumerable<TSymbol> explicitImplementations,
        ISymbol interfaceMember)
        where TSymbol : ISymbol
    {
        var interfaceLookupKey = RoslynSymbolUtilities.GetLookupKey(
            RoslynSymbolUtilities.Canonicalize(interfaceMember));

        foreach (var explicitImplementation in explicitImplementations)
        {
            var explicitLookupKey = RoslynSymbolUtilities.GetLookupKey(
                RoslynSymbolUtilities.Canonicalize(explicitImplementation));
            if (string.Equals(explicitLookupKey, interfaceLookupKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HaveSameParameters(
        IReadOnlyList<IParameterSymbol> candidateParameters,
        IReadOnlyList<IParameterSymbol> interfaceParameters)
    {
        if (candidateParameters.Count != interfaceParameters.Count)
        {
            return false;
        }

        for (var index = 0; index < candidateParameters.Count; index++)
        {
            var candidateParameter = candidateParameters[index];
            var interfaceParameter = interfaceParameters[index];

            if (candidateParameter.RefKind != interfaceParameter.RefKind)
            {
                return false;
            }

            if (!HaveSameType(candidateParameter.Type, interfaceParameter.Type))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HaveSameType(ITypeSymbol candidateType, ITypeSymbol interfaceType)
    {
        var candidateLookupKey = RoslynSymbolUtilities.GetLookupKey(
            RoslynSymbolUtilities.Canonicalize(candidateType));
        var interfaceLookupKey = RoslynSymbolUtilities.GetLookupKey(
            RoslynSymbolUtilities.Canonicalize(interfaceType));

        return string.Equals(candidateLookupKey, interfaceLookupKey, StringComparison.Ordinal);
    }
}
