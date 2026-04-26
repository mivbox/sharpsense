using JetBrains.Annotations;

namespace SharpSense.Application.Features.Indexing.Contracts;

[PublicAPI]
public sealed record DiscoveredFile(
    string AbsolutePath,
    string RelativeFilePath);
