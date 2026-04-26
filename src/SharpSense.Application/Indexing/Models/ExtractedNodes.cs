using JetBrains.Annotations;

namespace SharpSense.Application.Indexing.Models;

[PublicAPI]
public sealed record ExtractedNodes(
    IReadOnlyList<IndexedProject> Projects,
    IReadOnlyList<IndexedCodeNode> CodeNodes,
    IReadOnlyList<IndexedDependency> Edges,
    IReadOnlyList<string> Diagnostics);
