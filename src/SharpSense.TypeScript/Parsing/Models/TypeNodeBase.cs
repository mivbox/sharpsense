namespace SharpSense.TypeScript.Parsing.Models;

public class TypeNodeBase(SyntaxKind kind)
    : Node(kind), ITypeNode
{
}
