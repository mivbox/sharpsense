namespace SharpSense.TypeScript.Parsing.Models;

public class JSDocParameterTag() : JSDocTag(SyntaxKind.JSDocParameterTag), IGetRestChildren
{
    public Identifier? PreParameterName { get; internal set; }

    public JSDocTypeExpression? TypeExpression { get; internal set; }

    public Identifier? PostParameterName { get; internal set; }

    public Identifier? ParameterName { get; internal set; }

    public bool IsBracketed { get; internal set; }

    public override IEnumerable<INode?> GetRestChildren()
    {
        foreach (var x in base.GetRestChildren())
        {
            yield return x;
        }

        yield return PreParameterName;
        yield return TypeExpression;
        yield return PostParameterName;
        yield return ParameterName;
    }
}
