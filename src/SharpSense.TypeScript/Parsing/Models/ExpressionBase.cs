namespace SharpSense.TypeScript.Parsing.Models;

public class ExpressionBase(SyntaxKind kind)
    : Node(kind), IExpression
{
}
