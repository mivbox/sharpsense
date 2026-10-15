namespace SharpSense.TypeScript.Parsing.Models;

public class LeftHandSideExpressionBase(SyntaxKind kind)
    : UpdateExpressionBase(kind), ILeftHandSideExpression
{
}
