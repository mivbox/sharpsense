namespace SharpSense.TypeScript.Parsing.Models;

public class NamespaceExportDeclaration(Identifier name)
    : DeclarationStatement<Identifier>(SyntaxKind.NamespaceExportDeclaration, name), IHasModifiers
{
    public NodeArray<IModifierLike>? Modifiers { get; internal set; }
}
