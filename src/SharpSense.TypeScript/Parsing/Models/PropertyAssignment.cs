namespace SharpSense.TypeScript.Parsing.Models;

public class PropertyAssignment(IPropertyName name, IExpression? initializer)

    : ObjectLiteralElement<IPropertyName>(SyntaxKind.PropertyAssignment, name),
    IObjectLiteralElementLike,
    IVariableLikeDeclaration,
    IGetRestChildren,
    IHasModifiers
{
    public IExpression? Initializer { get; internal set; } = initializer;

    public NodeArray<IModifierLike>? Modifiers { get; internal set; } // for error reporting

    public INode? QuestionToken { get; internal set; }  // for error reporting

    public INode? ExclamationToken { get; internal set; }  // for error reporting

    public override IEnumerable<INode?> GetRestChildren()
    {
        return [Name, QuestionToken, Initializer, ExclamationToken];
    }
}
