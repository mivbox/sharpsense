
namespace SharpSense.TypeScript.Parsing.Models;

public class HeritageClause(SyntaxKind token, NodeArray<ExpressionWithTypeArguments> types)
    : Node(SyntaxKind.HeritageClause), IGetRestChildren
{
    public SyntaxKind Token { get; internal set; } = token; //  SyntaxKind.ExtendsKeyword | SyntaxKind.ImplementsKeyword

    public NodeArray<ExpressionWithTypeArguments> Types { get; } = types;

    public IEnumerable<INode?> GetRestChildren()
    {
        return Types;
    }
}
