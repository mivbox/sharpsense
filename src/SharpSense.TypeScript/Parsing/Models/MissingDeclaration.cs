namespace SharpSense.TypeScript.Parsing.Models;

public class MissingDeclaration()
    : NamedDeclaration<Identifier>(SyntaxKind.MissingDeclaration, null),
    IDeclarationStatement,
    IClassElement,
    IObjectLiteralElement,
    ITypeElement,
    IHasModifiers,
    IGetRestChildren,
    IPrimaryExpression
{
    public NodeArray<IModifierLike>? Modifiers { get; internal set; }

    public QuestionToken? QuestionToken { get; internal set; }

    public override IEnumerable<INode?> GetRestChildren()
    {
        return [Name, QuestionToken];
    }
}
