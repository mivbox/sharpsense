namespace SharpSense.TypeScript.Parsing.Models;

public class ParenthesizedTypeNode(ITypeNode type)
    : TypeNodeBase(SyntaxKind.ParenthesizedType), IGetRestChildren
{
    public ITypeNode Type { get; internal set; } = type;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [Type];
    }
}
