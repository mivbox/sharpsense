namespace SharpSense.TypeScript.Parsing.Models;

public class UnaryExpressionBase(SyntaxKind kind)
    : ExpressionBase(kind), IUnaryExpression
{
}
