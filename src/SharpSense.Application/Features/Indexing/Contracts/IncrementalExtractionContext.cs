using JetBrains.Annotations;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Features.Indexing.Contracts;

[PublicAPI]
public sealed record IncrementalExtractionContext(
    string TargetPath,
    IReadOnlyList<WorkspaceFileChange> ChangedFiles,
    IProgress<IndexingProgress>? Progress = null);
