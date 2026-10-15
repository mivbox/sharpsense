namespace SharpSense.TypeScript.Parsing.Models;

public class RegularExpressionLiteral(string text)
    : LiteralExpressionBase(SyntaxKind.RegularExpressionLiteral, text)
{
}
