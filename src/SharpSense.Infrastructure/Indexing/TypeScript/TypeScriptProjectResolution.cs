using SharpSense.Application.Indexing.Models;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed record TypeScriptProjectResolution(
    IReadOnlyList<IndexedProject> Projects,
    IReadOnlyDictionary<string, string> ProjectIdByFilePath,
    IReadOnlyCollection<string> ProjectInputPaths);
