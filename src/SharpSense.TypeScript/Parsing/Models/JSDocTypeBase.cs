namespace SharpSense.TypeScript.Parsing.Models;

public class JSDocTypeBase(SyntaxKind kind)
    : TypeNodeBase(kind), IJSDocType
{
}
