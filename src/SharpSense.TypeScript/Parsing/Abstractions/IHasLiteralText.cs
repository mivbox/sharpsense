namespace SharpSense.TypeScript.Parsing.Abstractions;


public interface IHasLiteralText : INode
{
    string? Text { get; }
}
