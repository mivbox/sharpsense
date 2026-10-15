namespace SharpSense.TypeScript.Parsing.Models;

public class TupleTypeNode(NodeArray<ITypeNode> elementTypes)
    : TypeNodeBase(SyntaxKind.TupleType), IGetRestChildren
{
    public NodeArray<ITypeNode> ElementTypes { get; internal set; } = elementTypes;

    public IEnumerable<INode?> GetRestChildren()
    {
        return ElementTypes;
    }
}
