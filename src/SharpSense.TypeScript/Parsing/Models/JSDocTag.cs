namespace SharpSense.TypeScript.Parsing.Models;

public class JSDocTag(SyntaxKind kind = SyntaxKind.JSDocTag) : Node(kind), IJSDocTag, IGetRestChildren
{
    public AtToken? AtToken { get; internal set; }

    public Identifier? TagName { get; internal set; }

    public string? Comment { get; internal set; }

    public virtual IEnumerable<INode?> GetRestChildren()
    {
        return [AtToken, TagName];
    }
}
