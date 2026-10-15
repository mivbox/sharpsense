namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface ISignatureDeclaration : INamedDeclaration, IDeclarationWithTypeParameterChildren
{
    NodeArray<ParameterDeclaration>? Parameters { get; }

    ITypeNode? Type { get; }
}
