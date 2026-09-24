using JetBrains.Annotations;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Indexing.Models;

/// <summary>
/// Requests a complete extraction. Changed files optionally allow reuse of a loaded workspace;
/// they never restrict the scope of the returned graph.
/// </summary>
[PublicAPI]
public sealed record ExtractionContext(
    string TargetPath,
    IProgress<IndexingProgress>? Progress,
    IReadOnlyList<WorkspaceFileChange>? ChangedFiles = null,
    IReadOnlyList<string>? IncludePatterns = null);
