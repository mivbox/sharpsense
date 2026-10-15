namespace SharpSense.TypeScript.Parsing.Models;

public class Identifier
    : PrimaryExpressionBase,
    IDeclaration,
    IJsxTagNameExpression,
    IEntityName,
    IPropertyName,
    IBindingName,
    IJsxAttributeName,
    IHasLiteralText,
    IModuleName,
    IModuleExportName
{
    public Identifier(
        string? text,
        SyntaxKind? originalKeywordKind = null,
        bool? hasExtendedUnicodeEscape = null,
        SyntaxKind kind = SyntaxKind.Identifier)
        : base(kind)
    {
        if (originalKeywordKind == null && !string.IsNullOrEmpty(text))
        {
            originalKeywordKind = Scanner.StringToToken(text!);
        }

        if (originalKeywordKind == SyntaxKind.Identifier)
        {
            originalKeywordKind = null;
        }

        Text = text;
        OriginalKeywordKind = originalKeywordKind;
        HasExtendedUnicodeEscape = hasExtendedUnicodeEscape ?? false;

        if (HasExtendedUnicodeEscape)
        {
            Flags |= NodeFlags.IdentifierHasExtendedUnicodeEscape;
        }
    }

    public string? Text { get; internal set; }

    public SyntaxKind? OriginalKeywordKind { get; internal set; }

    public bool IsInJsDocNamespace { get; internal set; }

    public bool HasExtendedUnicodeEscape { get; internal set; }

    string? _escapedText;

    public string? EscapedText
    {
        get { return Text ?? _escapedText; }
        internal set { _escapedText = value; }
    }
}
