using JetBrains.Annotations;

namespace SharpSense.Application.Features.Indexing.Contracts;

[PublicAPI]
public sealed record IndexingSummary(
    string SolutionPath,
    int ProjectCount,
    int CodeNodeCount,
    int DependencyCount);
