namespace SharpSense.TypeScript.Parsing.Models;

internal sealed class DiagnosticMessage
{
    public string? Key { get; internal set; }

    public DiagnosticCategory Category { get; internal set; }

    public int Code { get; internal set; }

    public string? Message { get; internal set; }

    public Dictionary<string, bool>? ReportsUnnecessary { get; internal set; }

    public Dictionary<string, bool>? ReportsDeprecated { get; internal set; }

    internal bool? ElidedInCompatabilityPyramid { get; set; }
}
