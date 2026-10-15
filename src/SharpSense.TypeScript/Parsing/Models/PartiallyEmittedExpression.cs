namespace SharpSense.TypeScript.Parsing.Models;

public class PartiallyEmittedExpression(IExpression expression)
    : LeftHandSideExpressionBase(SyntaxKind.PartiallyEmittedExpression), IGetRestChildren
{
    public IExpression Expression { get; } = expression;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [Expression];
    }
}
