namespace SharpSense.TypeScript.Parsing.Models;

/// <summary>Per-file resource limits. Lengths and offsets use UTF-16 code units.</summary>
public sealed record TypeScriptParseOptions
{
    public int MaxSourceLength { get; init; } = 16 * 1024 * 1024;

    /// <summary>Maximum simultaneously active grammar productions, including speculative parsing.</summary>
    public int MaxRecursionDepth { get; init; } = 256;

    /// <summary>Maximum nodes completed, including nodes discarded during lookahead.</summary>
    public int MaxNodeCount { get; init; } = 1_000_000;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSourceLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxRecursionDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxRecursionDepth, 512);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxNodeCount);
    }
}
