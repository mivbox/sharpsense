namespace SharpSense.TypeScript.Parsing.Models;

public class JSDocTypeExpression() : Node(SyntaxKind.JSDocTypeExpression), IGetRestChildren
{
    public ITypeNode? Type { get; internal set; }

    public IEnumerable<INode?> GetRestChildren()
    {
        return [Type];
    }
}
