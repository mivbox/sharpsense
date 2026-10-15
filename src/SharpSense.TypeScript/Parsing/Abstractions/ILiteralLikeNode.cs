namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface ILiteralLikeNode : INode
{
    string? Text { get; }

    bool IsUnterminated { get; }

    bool HasExtendedUnicodeEscape { get; }

    bool IsOctalLiteral { get; }
}
