namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IBreakOrContinueStatement : IStatement
{
    Identifier? Label { get; }
}
