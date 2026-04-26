using JetBrains.Annotations;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Indexing.Models;

[PublicAPI]
public sealed record ExtractionContext(
    string TargetPath,
    IProgress<IndexingProgress>? Progress);
