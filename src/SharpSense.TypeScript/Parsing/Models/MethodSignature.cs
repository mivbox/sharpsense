namespace SharpSense.TypeScript.Parsing.Models;

public class MethodSignature(
    NodeArray<IModifierLike>? modifiers,
    IDeclarationName name,
    QuestionToken? questionToken,
    NodeArray<TypeParameterDeclaration>? typeParameters,
    NodeArray<ParameterDeclaration>? parameters,
    ITypeNode? type)

    : SignatureDeclarationBase(SyntaxKind.MethodSignature, name, typeParameters, parameters, type),
    ITypeElement,
    IMethodOrAccessorDeclaration
{
    public NodeArray<IModifierLike>? Modifiers { get; internal set; } = modifiers;

    public AsteriskToken? AsteriskToken { get; internal set; }

    public IBlockOrExpression? Body { get; internal set; } //  Block | Expression

    public QuestionToken? QuestionToken { get; internal set; } = questionToken;

    public override IEnumerable<INode?> GetRestChildren()
    {
        yield return Name;
        yield return QuestionToken;
        if (TypeParameters != null)
        {
            foreach (var x in TypeParameters)
            {
                yield return x;
            }
        }

        if (Parameters != null)
        {
            foreach (var x in Parameters)
            {
                yield return x;
            }
        }

        yield return Type;
    }
}
