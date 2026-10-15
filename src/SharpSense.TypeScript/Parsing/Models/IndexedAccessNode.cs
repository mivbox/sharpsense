namespace SharpSense.TypeScript.Parsing.Models;

public class IndexedAccessTypeNode(ITypeNode objectType, ITypeNode indexType)
    : TypeNodeBase(SyntaxKind.IndexedAccessType), IGetRestChildren
{
    public ITypeNode ObjectType { get; internal set; } = objectType;

    public ITypeNode IndexType { get; internal set; } = indexType;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [ObjectType, IndexType];
    }
}
