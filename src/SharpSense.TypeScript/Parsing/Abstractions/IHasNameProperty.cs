namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IHasNameProperty : INode
{
    IDeclarationName? Name { get; }
}
