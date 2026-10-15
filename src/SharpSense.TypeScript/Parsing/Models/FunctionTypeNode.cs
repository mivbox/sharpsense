namespace SharpSense.TypeScript.Parsing.Models;

public class FunctionTypeNode(
    NodeArray<TypeParameterDeclaration> typeParameters,
    NodeArray<ParameterDeclaration> parameters,
    ITypeNode type)
    : FunctionOrConstructorTypeNodeBase(SyntaxKind.FunctionType, typeParameters, parameters, type)
{
}
