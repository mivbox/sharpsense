using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal static class RoslynSymbolUtilities
{
    private static readonly SymbolDisplayFormat _fullyQualifiedNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType |
                       SymbolDisplayMemberOptions.IncludeParameters |
                       SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType |
                          SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat _summaryFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType |
                       SymbolDisplayMemberOptions.IncludeParameters |
                       SymbolDisplayMemberOptions.IncludeExplicitInterface |
                       SymbolDisplayMemberOptions.IncludeType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeName |
                          SymbolDisplayParameterOptions.IncludeType |
                          SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static ISymbol Canonicalize(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol methodSymbol => methodSymbol.ReducedFrom?.OriginalDefinition ?? methodSymbol.OriginalDefinition,
            INamedTypeSymbol namedTypeSymbol => namedTypeSymbol.OriginalDefinition,
            _ => symbol
        };
    }

    public static string GetLookupKey(ISymbol symbol)
    {
        return symbol.GetDocumentationCommentId()
               ?? GetFullyQualifiedName(symbol);
    }

    public static string GetFullyQualifiedName(ISymbol symbol)
    {
        return symbol.ToDisplayString(_fullyQualifiedNameFormat);
    }

    public static string GetNodeId(string projectId, ISymbol symbol)
    {
        return $"code:{projectId}:{GetLookupKey(symbol)}";
    }

    public static ISymbol? ResolveReferencedSymbol(SymbolInfo symbolInfo)
    {
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }

    public static string ExtractSummary(ISymbol symbol, NodeType nodeType)
    {
        var documentation = symbol.GetDocumentationCommentXml(expandIncludes: false);
        if (!string.IsNullOrWhiteSpace(documentation))
        {
            var summary = Regex.Replace(documentation, "<.*?>", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return summary;
            }
        }

        var descriptor = nodeType switch
        {
            NodeType.Class => "Class",
            NodeType.Interface => "Interface",
            NodeType.Method => "Method",
            NodeType.Property => "Property",
            NodeType.Field => "Field",
            NodeType.Document => "Document",
            _ => nodeType.ToString()
        };

        return $"{descriptor} {symbol.ToDisplayString(_summaryFormat)}";
    }
}
