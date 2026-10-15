namespace SharpSense.TypeScript.Parsing.Models;

public class ArrayTypeNode(ITypeNode elementType)
    : TypeNodeBase(SyntaxKind.ArrayType), IGetRestChildren
{
    public ITypeNode ElementType { get; internal set; } = elementType;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [ElementType];
    }
}
