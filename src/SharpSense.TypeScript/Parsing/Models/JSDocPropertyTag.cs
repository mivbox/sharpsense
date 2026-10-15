namespace SharpSense.TypeScript.Parsing.Models;

public class JSDocPropertyTag() : Node(SyntaxKind.JSDocPropertyTag), IJSDocTag, ITypeElement, IHasNameProperty, IGetRestChildren
{
    public JSDocTypeExpression? TypeExpression { get; internal set; }

    public AtToken? AtToken { get; internal set; }

    public Identifier? TagName { get; internal set; }

    public string? Comment { get; internal set; }

    public IDeclarationName? Name { get; internal set; }

    public QuestionToken? QuestionToken { get; internal set; }

    public IEnumerable<INode?> GetRestChildren()
    {
        return [TypeExpression, AtToken, TagName, AtToken, Name, QuestionToken];
    }
}
