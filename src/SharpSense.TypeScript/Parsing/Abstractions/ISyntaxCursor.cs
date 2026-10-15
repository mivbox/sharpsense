namespace SharpSense.TypeScript.Parsing.Abstractions;

internal interface ISyntaxCursor
{
    public INode? CurrentNode(int pos);
}
