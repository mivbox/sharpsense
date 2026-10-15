namespace SharpSense.TypeScript.Parsing.Models;

public class JsxAttributes(NodeArray<IObjectLiteralElement> properties)
    : ObjectLiteralExpressionBase<IObjectLiteralElement>(SyntaxKind.JsxAttributes, properties) // JsxAttributeLike>
{
}
