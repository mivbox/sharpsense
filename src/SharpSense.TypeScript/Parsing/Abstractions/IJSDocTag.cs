namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IJSDocTag : INode
{
    AtToken? AtToken { get; }

    Identifier? TagName { get; }

    string? Comment { get; }
}
