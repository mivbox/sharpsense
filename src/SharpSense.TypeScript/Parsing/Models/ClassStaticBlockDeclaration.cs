namespace SharpSense.TypeScript.Parsing.Models;

public class ClassStaticBlockDeclaration(Block body)
    : ClassElement(SyntaxKind.ClassStaticBlockDeclaration, name: null), IGetRestChildren, IHasModifiers
{
    public NodeArray<IModifierLike>? Modifiers { get; internal set; }

    public Block Body { get; } = body;

    public override IEnumerable<INode?> GetRestChildren()
    {
        return [Name, Body];
    }
}
