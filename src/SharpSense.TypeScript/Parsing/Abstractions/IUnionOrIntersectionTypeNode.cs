namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IUnionOrIntersectionTypeNode : ITypeNode
{
    NodeArray<ITypeNode>? Types { get; }
}
