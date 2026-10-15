namespace SharpSense.TypeScript.Parsing.Models;

public class NamespaceImport(Identifier name)
    : NamedDeclaration<Identifier>(SyntaxKind.NamespaceImport, name), INamedImportBindings
{
}
