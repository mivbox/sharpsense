namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface INode : ITextRange
{
    SyntaxKind Kind { get; }

    NodeFlags Flags { get; }

    INode? Parent { get; }

    string ToString(bool withPos);
}
