namespace SharpSense.TypeScript.Parsing.Models;

public class ObjectLiteralExpression(NodeArray<IObjectLiteralElementLike> properties, bool multiLine)

    : ObjectLiteralExpressionBase<IObjectLiteralElementLike>(SyntaxKind.ObjectLiteralExpression, properties)
{
    public bool MultiLine { get; } = multiLine;
}
