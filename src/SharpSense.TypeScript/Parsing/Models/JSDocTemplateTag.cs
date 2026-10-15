namespace SharpSense.TypeScript.Parsing.Models;

public class JSDocTemplateTag() : JSDocTag(SyntaxKind.JSDocTemplateTag), IDeclarationWithTypeParameterChildren
{
    public NodeArray<TypeParameterDeclaration>? TypeParameters { get; internal set; }
}
