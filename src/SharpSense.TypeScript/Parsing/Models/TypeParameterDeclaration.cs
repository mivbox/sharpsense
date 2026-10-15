namespace SharpSense.TypeScript.Parsing.Models;

public class TypeParameterDeclaration(
    NodeArray<IModifierLike>? modifiers,
    Identifier name,
    ITypeNode? constraint,
    ITypeNode? defaultType = null)
    : NamedDeclaration<Identifier>(SyntaxKind.TypeParameter, name), IHasModifiers, IGetRestChildren
{
    public NodeArray<IModifierLike>? Modifiers { get; internal set; } = modifiers;

    public ITypeNode? Constraint { get; } = constraint;

    public ITypeNode? Default { get; } = defaultType;

    public IExpression? Expression { get; internal set; }

    public override IEnumerable<INode?> GetRestChildren()
    {
        return [Name, Constraint, Default, Expression];
    }
}
