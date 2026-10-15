namespace SharpSense.TypeScript.Parsing.Models;

public class PrimaryExpressionToken : Token, IPrimaryExpression
{
    public PrimaryExpressionToken()
        : base(SyntaxKind.Unknown)
    {
    }
}
