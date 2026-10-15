namespace SharpSense.TypeScript.Parsing.Models;

public class NamespaceExport(IModuleExportName name)
    : NamedDeclaration<IModuleExportName>(SyntaxKind.NamespaceExport, name), INamedExportBindings
{
}
