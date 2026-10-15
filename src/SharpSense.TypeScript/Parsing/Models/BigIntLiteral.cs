namespace SharpSense.TypeScript.Parsing.Models;

public class BigIntLiteral(string text)
    :
    LiteralExpressionBase(SyntaxKind.BigIntLiteral, text),
    IPropertyName
{
}
