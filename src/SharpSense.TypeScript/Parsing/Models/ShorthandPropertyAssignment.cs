namespace SharpSense.TypeScript.Parsing.Models;

public class ShorthandPropertyAssignment(Identifier name, IExpression? objectAssignmentInitializer)

    : ObjectLiteralElement<Identifier>(SyntaxKind.ShorthandPropertyAssignment, name),
    IObjectLiteralElementLike,
    IGetRestChildren
{
    public EqualsToken? EqualsToken { get; internal set; }

    public IExpression? ObjectAssignmentInitializer { get; } = objectAssignmentInitializer;

    public QuestionToken? QuestionToken { get; internal set; }

    public ExclamationToken? ExclamationToken { get; internal set; }

    public override IEnumerable<INode?> GetRestChildren()
    {
        return [Name, EqualsToken, ObjectAssignmentInitializer, QuestionToken, ExclamationToken];
    }
}
