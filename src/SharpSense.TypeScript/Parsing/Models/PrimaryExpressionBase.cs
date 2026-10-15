namespace SharpSense.TypeScript.Parsing.Models;

public class PrimaryExpressionBase(SyntaxKind kind)
    : MemberExpressionBase(kind), IPrimaryExpression, IJsxTagNameExpression
{
}
