
namespace SharpSense.TypeScript.Parsing.Models;

public class ContinueStatement(Identifier? label = null)

    : Statement(SyntaxKind.ContinueStatement),
    IBreakOrContinueStatement,
    IFlowContainer,
    IGetRestChildren
{

    public Identifier? Label { get; } = label;

    public IEnumerable<INode?> GetRestChildren()
    {
        return [Label];
    }
}
