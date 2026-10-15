namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IAccessorDeclaration
    : IMethodOrAccessorDeclaration,
    ISignatureDeclaration,
    ITypeElement,
    IObjectLiteralElementLike,
    IDeclarationWithTypeParameterChildren
{
}
