namespace SharpSense.TypeScript.Parsing.Models;

public class NamedTupleMember(
    DotDotDotToken dotDotDotToken,
    Identifier name,
    QuestionToken questionToken,
    ITypeNode type)
    : TypeNodeBase(SyntaxKind.NamedTupleMember), IDeclaration, IGetRestChildren
{
    public DotDotDotToken DotDotDotToken { get; internal set; } = dotDotDotToken;

    public Identifier Name { get; internal set; } = name;

    public QuestionToken QuestionToken { get; internal set; } = questionToken;

    public ITypeNode Type { get; internal set; } = type;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [DotDotDotToken, Name, QuestionToken, Type];
    }
}
