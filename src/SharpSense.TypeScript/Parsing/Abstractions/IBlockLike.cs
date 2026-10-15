namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IBlockLike : INode
{
    NodeArray<IStatement>? Statements { get; }
}
