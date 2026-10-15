namespace SharpSense.TypeScript.Parsing.Models;

public class VariableDeclaration(
    IBindingName name,
    ExclamationToken? exclamationToken,
    ITypeNode? type,
    IExpression? initializer)

    : NamedDeclaration<IBindingName>(SyntaxKind.VariableDeclaration, name),
    IVariableLikeDeclaration,
    IGetRestChildren
{
    public ExclamationToken? ExclamationToken { get; } = exclamationToken;

    public ITypeNode? Type { get; internal set; } = type;

    public IExpression? Initializer { get; internal set; } = initializer;

    public override IEnumerable<INode?> GetRestChildren()
    {
        return [Name, ExclamationToken, Type, Initializer];
    }
}
