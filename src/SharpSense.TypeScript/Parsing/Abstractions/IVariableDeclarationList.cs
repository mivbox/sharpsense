namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IVariableDeclarationList : INode, IForInitializer
{
    NodeArray<VariableDeclaration>? Declarations { get; }
}
