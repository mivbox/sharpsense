namespace SharpSense.TypeScript.Parsing.Models;

public class FunctionOrConstructorTypeNodeBase(
    SyntaxKind kind,
    NodeArray<TypeParameterDeclaration> typeParameters,
    NodeArray<ParameterDeclaration> parameters,
    ITypeNode type)

    : SignatureDeclarationBase(kind, name: null, typeParameters, parameters, type),
    IFunctionOrConstructorTypeNode
{
}
