namespace SharpSense.TypeScript.Parsing.Models;


public class LiteralLikeNode
    : Node, ILiteralLikeNode, IHasLiteralText
{
    internal LiteralLikeNode(SyntaxKind kind, string? text) : base(kind)
    {
        Text = text;
    }

    public string? Text { get; internal set; }

    public bool IsUnterminated { get; internal set; }

    public bool HasExtendedUnicodeEscape { get; internal set; }

    public bool IsOctalLiteral { get; internal set; }
}
