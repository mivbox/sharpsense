namespace SharpSense.TypeScript.Parsing.Abstractions;


public interface IHasModifierLike : INode
{
    NodeArray<IModifierLike>? Modifiers { get; }
}
