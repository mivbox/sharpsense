namespace SharpSense.TypeScript.Parsing.Models;

public class UnionTypeNode(NodeArray<ITypeNode>? types)
    : TypeNodeBase(SyntaxKind.UnionType), IUnionOrIntersectionTypeNode, IGetRestChildren
{
    public NodeArray<ITypeNode>? Types { get; internal set; } = types;

    public IEnumerable<INode?> GetRestChildren()
    {
        return Types ?? [];
    }
}
