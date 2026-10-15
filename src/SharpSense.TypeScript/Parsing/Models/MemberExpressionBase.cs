namespace SharpSense.TypeScript.Parsing.Models;

public class MemberExpressionBase(SyntaxKind kind)
    : LeftHandSideExpressionBase(kind), IMemberExpression
{
}
