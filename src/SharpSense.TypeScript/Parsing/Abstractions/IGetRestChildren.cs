namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IGetRestChildren : INode
{
    IEnumerable<INode?> GetRestChildren();
}
