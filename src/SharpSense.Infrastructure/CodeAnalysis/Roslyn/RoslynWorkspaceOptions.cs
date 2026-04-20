namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

public sealed class RoslynWorkspaceOptions
{
    public bool SkipUnrecognizedProjects { get; init; } = true;

    public bool LoadMetadataForReferencedProjects { get; init; }

    public IReadOnlyDictionary<string, string> MSBuildProperties { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
