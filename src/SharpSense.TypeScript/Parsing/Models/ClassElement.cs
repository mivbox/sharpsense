namespace SharpSense.TypeScript.Parsing.Models;

public class ClassElement(SyntaxKind kind, IPropertyName? name)
    : NamedDeclaration<IPropertyName>(kind, name), IClassElement
{
}
