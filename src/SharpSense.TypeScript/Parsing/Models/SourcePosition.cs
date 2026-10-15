namespace SharpSense.TypeScript.Parsing.Models;

/// <summary>A one-based line and UTF-16 column in a source file.</summary>
public readonly record struct SourcePosition(int Line, int Column);
