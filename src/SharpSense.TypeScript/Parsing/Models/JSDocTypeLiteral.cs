namespace SharpSense.TypeScript.Parsing.Models;

public class JSDocTypeLiteral() : JSDocTypeBase(SyntaxKind.JSDocTypeLiteral), IGetRestChildren
{
    public NodeArray<JSDocPropertyTag>? JSDocPropertyTags { get; internal set; }

    public JSDocTypeTag? JSDocTypeTag { get; internal set; }

    public IEnumerable<INode?> GetRestChildren()
    {
        if (JSDocPropertyTags != null)
        {
            foreach (var x in JSDocPropertyTags)
            {
                yield return x;
            }
        }

        yield return JSDocTypeTag;
    }
}
