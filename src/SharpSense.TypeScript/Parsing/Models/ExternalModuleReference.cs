namespace SharpSense.TypeScript.Parsing.Models;

public class ExternalModuleReference(IExpression expression)
    : Node(SyntaxKind.ExternalModuleReference), IGetRestChildren, IModuleReference
{
    public IExpression Expression { get; } = expression;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [Expression];
    }
}
