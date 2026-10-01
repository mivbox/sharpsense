namespace SharpSense.Application.GraphStats.Models;

public sealed record LanguageStats(
    string Language,
    long FileCount,
    long NodeCount,
    long EmbeddedNodeCount);
