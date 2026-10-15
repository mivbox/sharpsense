namespace SharpSense.TypeScript.Parsing.Models;

public class IntersectionTypeNode(NodeArray<ITypeNode>? types)
    : TypeNodeBase(SyntaxKind.IntersectionType), IUnionOrIntersectionTypeNode, IGetRestChildren
{
    public NodeArray<ITypeNode>? Types { get; internal set; } = types;

    public IEnumerable<INode?> GetRestChildren()
    {
        return Types ?? [];
    }
}
