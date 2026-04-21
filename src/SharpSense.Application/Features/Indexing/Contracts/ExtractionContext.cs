using JetBrains.Annotations;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Features.Indexing.Contracts;

[PublicAPI]
public sealed record ExtractionContext(
    string TargetPath,
    IProgress<IndexingProgress>? Progress);
