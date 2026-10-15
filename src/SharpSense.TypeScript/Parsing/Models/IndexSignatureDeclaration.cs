namespace SharpSense.TypeScript.Parsing.Models;

public class IndexSignatureDeclaration(
    NodeArray<IModifierLike>? modifiers,
    NodeArray<ParameterDeclaration> parameters,
    ITypeNode type)

    : SignatureDeclarationBase(SyntaxKind.IndexSignature, name: null, null, parameters, type),
    IClassElement,
    ITypeElement,
    IHasModifiers
{
    public NodeArray<IModifierLike>? Modifiers { get; } = modifiers;
}
