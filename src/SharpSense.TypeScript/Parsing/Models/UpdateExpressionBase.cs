namespace SharpSense.TypeScript.Parsing.Models;

public class UpdateExpressionBase(SyntaxKind kind)
    : UnaryExpressionBase(kind), IUpdateExpression
{
}
