namespace SharpSense.TypeScript.Parsing.Models;

/// <summary>The source exceeded a parser resource limit; no partial syntax tree is returned.</summary>
public sealed class TypeScriptParseLimitException : Exception
{
    internal TypeScriptParseLimitException(string limit, int offset)
        : base($"TypeScript parsing exceeded {limit} at UTF-16 offset {offset}.")
    {
        Limit = limit;
        Offset = offset;
    }

    public string Limit { get; }

    public int Offset { get; }
}
