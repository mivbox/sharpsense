namespace SharpSense.TypeScript.Parsing.Models;

public class PropertySignature(
    NodeArray<IModifierLike>? modifiers,
    IPropertyName name,
    QuestionToken? questionToken,
    ITypeNode? type)

    : TypeElement(SyntaxKind.PropertySignature, name, questionToken),
    IVariableLikeDeclaration,
    IHasModifiers,
    IGetRestChildren
{
    public NodeArray<IModifierLike>? Modifiers { get; } = modifiers;

    public ITypeNode? Type { get; internal set; } = type;

    public IExpression? Initializer { get; internal set; } // error reporting only

    public override IEnumerable<INode?> GetRestChildren()
    {
        return [Name, QuestionToken, Type, Initializer];
    }
}
