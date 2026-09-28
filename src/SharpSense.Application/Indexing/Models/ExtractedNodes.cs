using JetBrains.Annotations;

namespace SharpSense.Application.Indexing.Models;

[PublicAPI]
// Reuse is opt-in: InputPaths must include declared semantic inputs beyond source files.
// Coordinator still refreshes C# on documentation additions/renames to reevaluate input globs.
public sealed record ExtractedNodes(
    IReadOnlyList<IndexedProject> Projects,
    IReadOnlyList<IndexedCodeNode> CodeNodes,
    IReadOnlyList<IndexedDependency> Edges,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string>? InputPaths = null,
    bool CanReuseForDocumentationChanges = false);
