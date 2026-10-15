namespace SharpSense.TypeScript.Parsing.Models;

public class TypeElement(SyntaxKind kind, IPropertyName? name, QuestionToken? questionToken)
    : NamedDeclaration<IPropertyName>(kind, name), ITypeElement, IGetRestChildren
{
    public QuestionToken? QuestionToken { get; internal set; } = questionToken;

    public override IEnumerable<INode?> GetRestChildren()
    {
        return [Name, QuestionToken];
    }
}
