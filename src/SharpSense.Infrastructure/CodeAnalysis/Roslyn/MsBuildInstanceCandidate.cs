using Microsoft.Build.Locator;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

public sealed record MsBuildInstanceCandidate(
    string Name,
    string MSBuildPath,
    Version Version,
    DiscoveryType DiscoveryType);
