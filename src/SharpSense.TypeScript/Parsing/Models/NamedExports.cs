namespace SharpSense.TypeScript.Parsing.Models;

public class NamedExports(NodeArray<ExportSpecifier> elements)
    : Node(SyntaxKind.NamedExports), INamedImportsOrExports, INamedExportBindings, IGetRestChildren
{
    public NodeArray<ExportSpecifier> Elements { get; } = elements;

    public IEnumerable<INode?> GetRestChildren()
    {
        return Elements;
    }
}
