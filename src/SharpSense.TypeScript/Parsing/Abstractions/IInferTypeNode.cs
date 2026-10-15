namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IInferTypeNode : ITypeNode
{
    TypeParameterDeclaration? TypeParameter { get; }
}
