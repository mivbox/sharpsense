
namespace SharpSense.TypeScript.Parsing.Models;

public class ExpressionStatement(IExpression expression)
    : Statement(SyntaxKind.ExpressionStatement), IFlowContainer, IGetRestChildren
{
    public IExpression Expression { get; } = expression;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [Expression];
    }
}
