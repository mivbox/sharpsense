namespace SharpSense.TypeScript.Parsing.Models;

public class StringLiteral(string? text, bool? isSingleQuote = null, bool hasExtendedUnicodeEscape = false)

    : LiteralExpressionBase(
        SyntaxKind.StringLiteral,
        text,
        hasExtendedUnicodeEscape: hasExtendedUnicodeEscape),
    IPropertyName,
    IJsxAttributeValue,
    IGetRestChildren,
    IModuleName,
    IModuleExportName,
    IStringLiteralLike
{
    public bool? IsSingleQuote { get; internal set; } = isSingleQuote;

    public INode? TextSourceNode { get; internal set; } // Identifier | StringLiteral | NumericLiteral

    public IEnumerable<INode?> GetRestChildren()
    {
        return [TextSourceNode];
    }
}
