namespace SharpSense.TypeScript.Parsing.Models;

internal sealed class Diagnostic
{
    public SourceFile? File { get; internal set; }

    public string? FileName { get; internal set; }

    public int Start { get; internal set; }

    public int Length { get; internal set; }

    public DiagnosticMessage? Message { get; internal set; }

    public object? Argument { get; internal set; }
}
