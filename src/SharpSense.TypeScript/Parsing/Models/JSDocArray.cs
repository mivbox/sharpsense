namespace SharpSense.TypeScript.Parsing.Models;

public class JSDocArray : List<JSDoc>
{
    public JSDocArray()
    {
    }

    public JSDocArray(JSDoc[] elements)
        : base([.. elements])
    {
    }

    public List<IJSDocTag>? JSDocCache { get; internal set; }
}
