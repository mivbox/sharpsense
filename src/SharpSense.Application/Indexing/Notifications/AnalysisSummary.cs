namespace SharpSense.Application.Indexing.Notifications;

/// <summary>
/// Counts describe the complete committed workspace graph and the sources processed or reused for that operation.
/// </summary>
public sealed record AnalysisSummary(
    int Projects,
    int Nodes,
    int Edges,
    int Documents,
    int ExtractedSources,
    int ReusedSources,
    int ReusedEmbeddings,
    int GeneratedEmbeddings,
    int DiagnosticCount);
