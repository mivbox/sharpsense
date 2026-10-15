
namespace SharpSense.TypeScript.Parsing.Models;

public class CatchClause(VariableDeclaration? variableDeclaration, Block block)
    : Node(SyntaxKind.CatchClause), IGetRestChildren
{
    public VariableDeclaration? VariableDeclaration { get; internal set; } = variableDeclaration;

    public Block Block { get; internal set; } = block;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [VariableDeclaration, Block];
    }
}
