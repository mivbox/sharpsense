namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IFunctionLikeDeclaration : ISignatureDeclaration
{
    AsteriskToken? AsteriskToken { get; }

    QuestionToken? QuestionToken { get; }

    IBlockOrExpression? Body { get; } // Block | Expression
}
