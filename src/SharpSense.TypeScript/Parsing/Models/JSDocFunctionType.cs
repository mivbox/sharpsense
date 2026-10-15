namespace SharpSense.TypeScript.Parsing.Models;

public class JSDocFunctionType(NodeArray<ParameterDeclaration>? parameters, ITypeNode? type)

    : Node(SyntaxKind.JSDocFunctionType),
    IJSDocType,
    ISignatureDeclaration,
    IHasNameProperty,
    IGetRestChildren
{
    public NodeArray<ParameterDeclaration>? Parameters { get; internal set; } = parameters;

    public ITypeNode? Type { get; internal set; } = type;

    public IDeclarationName? Name { get; internal set; }

    public NodeArray<TypeParameterDeclaration>? TypeParameters { get; internal set; }

    public IEnumerable<INode?> GetRestChildren()
    {
        if (Parameters != null)
        {
            foreach (var x in Parameters)
            {
                yield return x;
            }
        }

        yield return Type;
        yield return Name;
        if (TypeParameters != null)
        {
            foreach (var x in TypeParameters)
            {
                yield return x;
            }
        }
    }
}
