namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface ITextRange
{
    int? Pos { get; }

    int? End { get; }
}
