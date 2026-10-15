namespace SharpSense.TypeScript.Parsing.Models;

public class OperatorToken : Token
{
    public OperatorToken(SyntaxKind kind)
    : base(kind)
    {
    }

    public OperatorToken()
        : base(SyntaxKind.Unknown)
    {
    }
}
