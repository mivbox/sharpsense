namespace SharpSense.TypeScript.Parsing.Models;

public class LiteralExpressionBase(SyntaxKind kind, string? text)
    : LiteralLikeNode(kind, text), ILiteralExpression, IPrimaryExpression
{
    internal LiteralExpressionBase(
        SyntaxKind kind,
        string? text,
        bool isUnterminated = false,
        bool hasExtendedUnicodeEscape = false,
        bool isOctalLiteral = false) : this(kind, text)
    {
        IsUnterminated = isUnterminated;
        HasExtendedUnicodeEscape = hasExtendedUnicodeEscape;
        IsOctalLiteral = isOctalLiteral;
    }
}
