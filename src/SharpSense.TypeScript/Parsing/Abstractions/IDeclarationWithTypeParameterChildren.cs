namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IDeclarationWithTypeParameterChildren : INode, IDeclarationWithTypeParameters
{
    NodeArray<TypeParameterDeclaration>? TypeParameters { get; }
}
