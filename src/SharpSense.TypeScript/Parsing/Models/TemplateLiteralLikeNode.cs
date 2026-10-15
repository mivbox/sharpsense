namespace SharpSense.TypeScript.Parsing.Models;

public class TemplateLiteralLikeNode(
    SyntaxKind kind,
    string? text,
    string? rawText,
    TokenFlags? templateFlags)
    : LiteralLikeNode(kind, text), ITemplateLiteralLikeNode
{
    public string? RawText { get; internal set; } = rawText;

    public TokenFlags? TemplateFlags { get; internal set; } = (templateFlags ?? TokenFlags.None) & TokenFlags.TemplateLiteralLikeFlags;
}
