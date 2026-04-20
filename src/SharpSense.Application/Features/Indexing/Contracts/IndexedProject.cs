using JetBrains.Annotations;

namespace SharpSense.Application.Features.Indexing.Contracts;

[PublicAPI]
public sealed record IndexedProject(
    string Id,
    string Name,
    string RelativeFilePath,
    string ContentHash);
