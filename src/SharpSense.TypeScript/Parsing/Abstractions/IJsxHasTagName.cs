namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IJsxHasTagName : INode
{
    IJsxTagNameExpression? TagName { get; }
}
