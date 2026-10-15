namespace SharpSense.TypeScript.Parsing.Models;

public class JSDocTypedefTag()
    : Node(SyntaxKind.JSDocTypedefTag),
    IJSDocTag,
    IHasNameProperty,
    IGetRestChildren,
    IDeclarationWithTypeParameters
{
    public INode? FullName { get; internal set; } // JSDocNamespaceDeclaration | Identifier

    public JSDocTypeExpression? TypeExpression { get; internal set; }

    public JSDocTypeLiteral? JSDocTypeLiteral { get; internal set; }

    public IDeclarationName? Name { get; internal set; }

    public AtToken? AtToken { get; internal set; }

    public Identifier? TagName { get; internal set; }

    public string? Comment { get; internal set; }

    public IEnumerable<INode?> GetRestChildren()
    {
        return [FullName, TypeExpression, JSDocTypeLiteral, Name, AtToken, TagName];
    }
}
