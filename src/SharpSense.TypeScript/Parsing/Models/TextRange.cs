namespace SharpSense.TypeScript.Parsing.Models;

public class TextRange : ITextRange
{
    public int? Pos { get; internal set; }

    public int? End { get; internal set; }
}
