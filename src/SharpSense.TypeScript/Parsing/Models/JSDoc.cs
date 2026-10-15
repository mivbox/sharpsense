namespace SharpSense.TypeScript.Parsing.Models;

public class JSDoc() : Node(SyntaxKind.JSDoc), IGetRestChildren
{
    public NodeArray<IJSDocTag>? Tags { get; internal set; }

    public string? Comment { get; internal set; }

    public IEnumerable<INode?> GetRestChildren()
    {
        if (Tags != null)
        {
            foreach (var x in Tags)
            {
                yield return x;
            }
        }
    }
}
