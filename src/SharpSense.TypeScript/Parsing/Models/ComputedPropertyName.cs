namespace SharpSense.TypeScript.Parsing.Models;

public class ComputedPropertyName(IExpression expression)
    : Node(SyntaxKind.ComputedPropertyName), IPropertyName, IGetRestChildren
{
    public IExpression Expression { get; } = expression;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [Expression];
    }
}
