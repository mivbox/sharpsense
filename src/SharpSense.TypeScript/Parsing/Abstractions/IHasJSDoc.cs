namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IHasJSDoc : INode
{
    JSDocArray? JSDoc { get; }
}
