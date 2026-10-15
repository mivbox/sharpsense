namespace SharpSense.TypeScript.Parsing.Models;

public class NullLiteral()
    : Node(SyntaxKind.NullKeyword), IPrimaryExpression, ITypeNode
{
}
