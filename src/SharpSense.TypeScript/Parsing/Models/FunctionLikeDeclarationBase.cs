namespace SharpSense.TypeScript.Parsing.Models;

public class FunctionLikeDeclarationBase(
    SyntaxKind kind,
    IDeclarationName? name,
    NodeArray<TypeParameterDeclaration>? typeParameters,
    NodeArray<ParameterDeclaration>? parameters,
    ITypeNode? type,
    AsteriskToken? asteriskToken = null,
    QuestionToken? questionToken = null,
    ExclamationToken? exclamationToken = null,
    IBlockOrExpression? body = null)
    : SignatureDeclarationBase(
        kind,
        name,
        typeParameters,
        parameters,
        type),
    IFunctionLikeDeclaration,
    IGetRestChildren
{
    public AsteriskToken? AsteriskToken { get; internal set; } = asteriskToken;

    public QuestionToken? QuestionToken { get; internal set; } = questionToken;

    public ExclamationToken? ExclamationToken { get; internal set; } = exclamationToken;

    public IBlockOrExpression? Body { get; internal set; } = body;//  Block | Expression

    public override IEnumerable<INode?> GetRestChildren()
    {
        yield return AsteriskToken;
        foreach (var x in base.GetRestChildren())
        {
            yield return x;
        }

        yield return QuestionToken;
        yield return ExclamationToken;
        yield return Body;
    }
}
