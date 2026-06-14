using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal static class RoslynSymbolUtilities
{
    private const string _searchTextSeparator = "\n";

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

    private static readonly SymbolDisplayFormat _displayNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType |
                       SymbolDisplayMemberOptions.IncludeParameters |
                       SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType |
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

    public static string GetCanonicalId(string projectId, ISymbol symbol)
    {
        return $"code:{projectId}:{GetLookupKey(symbol)}";
    }

    public static string GetDisplayName(ISymbol symbol)
        => symbol.ToDisplayString(_displayNameFormat);

    public static ISymbol? ResolveReferencedSymbol(SymbolInfo symbolInfo)
    {
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }

    public static string ExtractSummary(SyntaxNode declarationSyntax)
    {
        ArgumentNullException.ThrowIfNull(declarationSyntax);

        var documentationComment = declarationSyntax
            .GetLeadingTrivia()
            .Select(static trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();
        if (documentationComment is null)
        {
            return string.Empty;
        }

        var summary = ExtractDocumentationElementText(documentationComment, "summary");
        var remarks = ExtractDocumentationElementText(documentationComment, "remarks");
        return string.Join(
                _searchTextSeparator,
                new[] { summary, remarks }.Where(static text => !string.IsNullOrWhiteSpace(text)))
            .Trim();
    }

    public static string BuildSearchText(
        ISymbol symbol,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var displayName = GetDisplayName(symbol);
        return string.IsNullOrWhiteSpace(summary)
            ? displayName
            : $"{displayName}{_searchTextSeparator}{summary}";
    }

    public static string? ComputeBodyHash(SyntaxNode declarationSyntax)
    {
        ArgumentNullException.ThrowIfNull(declarationSyntax);

        var normalizedDeclaration = string.Concat(
            declarationSyntax
                .DescendantTokens()
                .Select(static token => token.Text));
        if (string.IsNullOrEmpty(normalizedDeclaration))
        {
            return null;
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDeclaration));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string ExtractDocumentationElementText(
        DocumentationCommentTriviaSyntax documentationComment,
        string elementName)
    {
        var text = documentationComment.Content
            .OfType<XmlElementSyntax>()
            .Where(element => string.Equals(
                element.StartTag.Name.LocalName.ValueText,
                elementName,
                StringComparison.Ordinal))
            .Select(element => FlattenXmlContent(element.Content))
            .Where(static value => !string.IsNullOrWhiteSpace(value));

        return string.Join(_searchTextSeparator, text).Trim();
    }

    private static string FlattenXmlContent(SyntaxList<XmlNodeSyntax> content)
    {
        var builder = new StringBuilder();

        foreach (var node in content)
        {
            AppendXmlNodeText(builder, node);
        }

        return NormalizeWhitespace(builder.ToString());
    }

    private static void AppendXmlNodeText(
        StringBuilder builder,
        XmlNodeSyntax node)
    {
        switch (node)
        {
            case XmlTextSyntax xmlText:
                foreach (var token in xmlText.TextTokens)
                {
                    builder.Append(token.ValueText);
                }

                break;

            case XmlElementSyntax xmlElement:
                foreach (var child in xmlElement.Content)
                {
                    AppendXmlNodeText(builder, child);
                }

                break;

            case XmlEmptyElementSyntax emptyElement:
                AppendXmlEmptyElementText(builder, emptyElement);
                break;

            case XmlCDataSectionSyntax cDataSection:
                builder.Append(cDataSection.ToFullString());
                break;
        }
    }

    private static void AppendXmlEmptyElementText(
        StringBuilder builder,
        XmlEmptyElementSyntax emptyElement)
    {
        var elementName = emptyElement.Name.LocalName.ValueText;
        var value = elementName switch
        {
            "see" => GetAttributeValue(emptyElement, "cref") ?? GetAttributeValue(emptyElement, "langword"),
            "paramref" or "typeparamref" => GetAttributeValue(emptyElement, "name"),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Length > 0 && !char.IsWhiteSpace(builder[^1]))
        {
            builder.Append(' ');
        }

        builder.Append(value);
    }

    private static string? GetAttributeValue(
        XmlEmptyElementSyntax emptyElement,
        string attributeName)
    {
        foreach (var attribute in emptyElement.Attributes)
        {
            switch (attribute)
            {
                case XmlCrefAttributeSyntax crefAttribute
                    when string.Equals(crefAttribute.Name.LocalName.ValueText, attributeName, StringComparison.Ordinal):
                    return crefAttribute.Cref.ToString();

                case XmlNameAttributeSyntax nameAttribute
                    when string.Equals(nameAttribute.Name.LocalName.ValueText, attributeName, StringComparison.Ordinal):
                    return nameAttribute.Identifier.Identifier.ValueText;

                case XmlTextAttributeSyntax textAttribute
                    when string.Equals(textAttribute.Name.LocalName.ValueText, attributeName, StringComparison.Ordinal):
                    return string.Concat(textAttribute.TextTokens.Select(static token => token.ValueText));
            }
        }

        return null;
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalizedBuilder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                normalizedBuilder.Append(' ');
                previousWasWhitespace = true;
                continue;
            }

            normalizedBuilder.Append(character);
            previousWasWhitespace = false;
        }

        return normalizedBuilder.ToString().Trim();
    }

}
