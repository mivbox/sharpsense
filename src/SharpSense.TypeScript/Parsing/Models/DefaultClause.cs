
namespace SharpSense.TypeScript.Parsing.Models;

public class DefaultClause(NodeArray<IStatement> statements)
    : Node(SyntaxKind.DefaultClause), ICaseOrDefaultClause, IGetRestChildren
{
    public NodeArray<IStatement> Statements { get; internal set; } = statements;

    public IEnumerable<INode?> GetRestChildren()
    {
        return Statements;
    }
}
