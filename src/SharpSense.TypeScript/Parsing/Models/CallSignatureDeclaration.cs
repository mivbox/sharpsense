namespace SharpSense.TypeScript.Parsing.Models;

public class CallSignatureDeclaration(
    NodeArray<TypeParameterDeclaration> typeParameters,
    NodeArray<ParameterDeclaration> parameters,
    ITypeNode type)

    : SignatureDeclarationBase(
        SyntaxKind.CallSignature,
        name: null,
        typeParameters,
        parameters,
        type),
    ISignatureDeclaration,
    ITypeElement
{
}
