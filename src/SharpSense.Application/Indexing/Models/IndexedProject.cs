using JetBrains.Annotations;

namespace SharpSense.Application.Indexing.Models;

[PublicAPI]
public sealed record IndexedProject(
    string Id,
    string Name,
    string RelativeFilePath,
    string ContentHash);
