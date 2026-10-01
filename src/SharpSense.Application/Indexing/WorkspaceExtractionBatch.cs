using SharpSense.Application.Indexing.Models;

namespace SharpSense.Application.Indexing;

internal sealed record WorkspaceExtractionBatch(
    string Key,
    IReadOnlyList<ExtractedNodes> Contributions,
    ExtractedNodes Graph);
