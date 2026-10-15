namespace SharpSense.TypeScript.Parsing.Models;

public class Node(SyntaxKind kind) : TextRange, INode
{
    public SyntaxKind Kind { get; internal set; } = kind;

    public NodeFlags Flags { get; internal set; }

    public INode? Parent { get; internal set; }

    public override string ToString()
    {
        var posStr = $" [{Pos}, {End}]";

        return $"{Enum.GetName(typeof(SyntaxKind), Kind)}  {posStr} {this.GetText(null)}";
    }

    public string ToString(bool withPos)
    {
        if (withPos)
        {
            return ToString();
        }

        return $"{Enum.GetName(typeof(SyntaxKind), Kind)}  {this.GetText(null)}";
    }
}
