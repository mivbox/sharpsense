using JetBrains.Annotations;

namespace SharpSense.Application.Indexing.Models;

[PublicAPI]
public sealed record DiscoveredFile(
    string AbsolutePath,
    string RelativeFilePath);
