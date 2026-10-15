namespace SharpSense.TypeScript.Parsing.Models;

public class LiteralTypeNode(IExpression literal)
    : TypeNodeBase(SyntaxKind.LiteralType), IGetRestChildren
{
    public IExpression Literal { get; internal set; } = literal;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [Literal];
    }
}
