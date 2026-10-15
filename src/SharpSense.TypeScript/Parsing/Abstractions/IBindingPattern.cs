namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IBindingPattern : INode, IBindingName
{
    NodeArray<IArrayBindingElement>? Elements { get; }
}
