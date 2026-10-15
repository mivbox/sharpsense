namespace SharpSense.TypeScript.Parsing.Models;

/// <summary>A syntax error with a TypeScript diagnostic code and one-based UTF-16 location.</summary>
public sealed record ParseDiagnostic(int Code, string Message, int Start, int Length, int Line, int Column);
