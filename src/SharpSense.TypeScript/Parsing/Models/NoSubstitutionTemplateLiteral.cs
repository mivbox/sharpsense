namespace SharpSense.TypeScript.Parsing.Models;

public class NoSubstitutionTemplateLiteral(string? text, string? rawText, TokenFlags templateFlags)

    : LiteralExpressionBase(SyntaxKind.NoSubstitutionTemplateLiteral, text),
    IPropertyName,
    ITemplateLiteralLikeNode,
    IDeclaration,
    IStringLiteralLike
{
    public string? RawText { get; internal set; } = rawText;

    public TokenFlags? TemplateFlags { get; internal set; } = templateFlags & TokenFlags.TemplateLiteralLikeFlags;
}
