
namespace SharpSense.TypeScript.Parsing.Models;

public class ReturnStatement(IExpression? expression)
    : Statement(SyntaxKind.ReturnStatement), IFlowContainer, IGetRestChildren
{
    public IExpression? Expression { get; } = expression;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [Expression];
    }
}
